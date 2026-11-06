using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Notifications;
using ClimbOn.Application.Outbox;
using ClimbOn.Infrastructure.Email;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Integration.Tests.Email;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClimbOn.Integration.Tests.Outbox;

// Design §4.1. Rows are written through OutboxQueue below the API, and delivered by the step the
// sweeper runs (RetryDueAsync), with senders that fail or wait as each test needs.
[Collection(EmailFailureCollection.Name)]
public sealed class OutboxTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Registration_email_is_delivered_and_its_row_cleared()
    {
        var account = await RegisterAsync();

        Assert.Equal(account.Email, Assert.Single(Factory.Email.Sent).To);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(Factory.Clock.UtcNow, row.SentAt);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.Recipient);
        Assert.Null(row.Subject);
        Assert.Null(row.Body);
    }

    // The outbox row is committed with the account, or not at all.
    [Fact]
    public async Task Refused_registration_writes_no_email()
    {
        var account = Factory.Data.Account(dateOfBirth: new DateOnly(2020, 1, 1));

        using var response = await Factory.CreateClient().PostAsJsonAsync(
            "/api/climber/accounts", Request(account), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsAsync());
        Assert.Empty(Factory.Email.Sent);
    }

    [Fact]
    public async Task Concurrent_deliverers_deliver_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rows) = await EnqueueAsync(30);
        var sender = new SlowSender();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await Outbox(scope, sender).RetryDueAsync(ct);
        }));

        Assert.Equal(30, sender.Sent.Count);
        Assert.Equal(30, sender.Sent.Select(message => message.Subject).Distinct().Count());
        var stored = await RowsAsync(rows);
        Assert.Equal(30, stored.Count);
        Assert.All(stored, row => Assert.NotNull(row.SentAt));
    }

    // A row another worker holds is skipped at once, never waited for: by the sweeper's claim, which
    // takes the next due row, and by the claim of that row by its ID, which finds nothing.
    [Fact]
    public async Task Claim_skips_a_locked_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rows) = await EnqueueAsync(2);
        await using var holderScope = Factory.Services.CreateAsyncScope();
        await using var held = await holderScope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimNextAsync(Factory.Clock.UtcNow, null, ct);
        Assert.NotNull(held);
        using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitLimit.CancelAfter(TimeSpan.FromSeconds(10));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await using var next = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .ClaimNextAsync(Factory.Clock.UtcNow, null, waitLimit.Token);
            Assert.NotNull(next);
            Assert.Equal(rows.Single(row => row != held.Item.Id), next.Item.Id);
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await using var same = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .ClaimNextAsync(Factory.Clock.UtcNow, held.Item.Id, waitLimit.Token);
            Assert.Null(same);
        }
    }

    // Design §4.1: the request delivers the rows it wrote; other due rows are the sweeper's.
    [Fact]
    public async Task Registration_delivers_only_its_own_email()
    {
        var (_, rows) = await EnqueueAsync(1);

        var account = await RegisterAsync();

        Assert.Equal(account.Email, Assert.Single(Factory.Email.Sent).To);
        var other = Assert.Single(await RowsAsync(rows));
        Assert.Null(other.SentAt);
        Assert.Equal(0, other.Attempts);
    }

    // The row is committed before delivery, so a failing store after commit fails no request: the row
    // stays due for the sweeper. When marking it sent fails, the sweeper sends it again (at least once).
    [Theory]
    [InlineData(BrokenStore.Fault.Claim)]
    [InlineData(BrokenStore.Fault.MarkSent)]
    public async Task After_commit_delivery_failure_is_left_to_the_sweeper(BrokenStore.Fault fault)
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rows) = await EnqueueAsync(1);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var store = new BrokenStore(scope.ServiceProvider.GetRequiredService<IOutboxStore>(), fault);
            await Outbox(scope, Factory.Email, store).DeliverAsync(rows[0], ct);
        }

        Assert.Equal(fault == BrokenStore.Fault.MarkSent ? 1 : 0, Factory.Email.Sent.Count);
        var row = Assert.Single(await RowsAsync(rows));
        Assert.Null(row.SentAt);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(Factory.Clock.UtcNow, row.NextAttemptAt);
        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        var log = Assert.Single(captured, item => item.Signal == "log" && Equals(item.Field("eventName"), "OutboxDeliveryFailed"));
        Assert.Equal(rows[0], log.Field("attribute:OutboxId"));

        await RetryAsync();
        Assert.NotNull(Assert.Single(await RowsAsync(rows)).SentAt);
    }

    [Fact]
    public async Task Failed_delivery_is_retried()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rows) = await EnqueueAsync(1);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await Outbox(scope, new FailingSender()).RetryDueAsync(ct);
        }

        var failed = Assert.Single(await RowsAsync(rows));
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(Factory.Clock.UtcNow + OutboxQueue.Backoff[0], failed.NextAttemptAt);

        // Not due again before the backoff has passed.
        await RetryAsync();
        Assert.Empty(Factory.Email.Sent);

        Factory.Clock.Advance(OutboxQueue.Backoff[0]);
        await RetryAsync();

        Assert.Single(Factory.Email.Sent);
        var sent = Assert.Single(await RowsAsync(rows));
        Assert.Equal(Factory.Clock.UtcNow, sent.SentAt);
        Assert.Null(sent.Body);
    }

    [Fact]
    public async Task Last_failed_attempt_marks_the_row_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, rows) = await EnqueueAsync(1);

        for (var attempt = 1; attempt <= OutboxQueue.MaximumAttempts; attempt++)
        {
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                await Outbox(scope, new FailingSender()).RetryDueAsync(ct);
            }

            if (attempt < OutboxQueue.MaximumAttempts)
            {
                Factory.Clock.Advance(OutboxQueue.Backoff[attempt - 1]);
            }
        }

        var row = Assert.Single(await RowsAsync(rows));
        Assert.Equal(OutboxQueue.MaximumAttempts, row.Attempts);
        Assert.Equal(Factory.Clock.UtcNow, row.FailedAt);
        Assert.Null(row.SentAt);
        Assert.Null(row.Recipient);
        Assert.Null(row.Body);

        // Failed is final.
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        await RetryAsync();
        Assert.Empty(Factory.Email.Sent);

        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        var log = Assert.Single(captured, item => item.Signal == "log" && Equals(item.Field("eventName"), "OutboxEmailFailed"));
        Assert.Equal(accountId, log.Field("attribute:AccountId"));
    }

    // C32: the SMTP server's refusal names the recipient; the C33 scan at the end of the test fails it
    // if the address reached a log or trace.
    [Fact]
    public async Task Smtp_failure_leaks_no_recipient_through_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = new StubSmtpServer(recipientReply: address => $"550 5.1.1 <{address}> unknown");
        var sender = ActivatorUtilities.CreateInstance<SmtpEmailSender>(
            Factory.Services,
            Options.Create(new EmailOptions { Sender = "noreply@climbon.test", Smtp = server.Smtp("tem-project-id") }),
            (ISmtpPasswordSource)new FixedPassword(),
            server.ClientFactory);
        var (_, rows) = await EnqueueAsync(1);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await Outbox(scope, sender).RetryDueAsync(ct);
        }

        Assert.Contains("RCPT", server.Commands);
        var row = Assert.Single(await RowsAsync(rows));
        Assert.Equal(1, row.Attempts);
        Assert.Equal(Factory.Clock.UtcNow + OutboxQueue.Backoff[0], row.NextAttemptAt);
        Assert.Null(row.SentAt);
        Assert.Null(row.FailedAt);
        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        Assert.Contains(captured, item => item.Signal == "log" && Equals(item.Field("eventName"), "EmailSendFailed")
            && Equals(item.Field("attribute:SmtpStatusCode"), 550));
    }

    // C29: the previous app version writes an email row without a kind, between the migration and the new app. It is
    // stored as an email and delivered.
    [Fact]
    public async Task Email_row_written_without_a_kind_is_an_email()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, _) = await EnqueueAsync(0);
        var id = Guid.CreateVersion7();
        var now = Factory.Clock.UtcNow;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Database.ExecuteSqlAsync(
                $"""
                INSERT INTO outbox (id, account_id, recipient, subject, body, created_at, attempts, next_attempt_at, sent_at, failed_at)
                VALUES ({id}, {accountId}, 'previous@climbon.test', 'Probe', 'A probe message.', {now}, 0, {now}, NULL, NULL)
                """,
                ct);
        }

        await RetryAsync();

        Assert.Equal("previous@climbon.test", Assert.Single(Factory.Email.Sent).To);
        var row = Assert.Single(await RowsAsync([id]));
        Assert.Equal(OutboxRow.EmailKind, row.Kind);
        Assert.NotNull(row.SentAt);
    }

    // Design §4.1: a notification event whose fan-out fails is retried with backoff, as an email is. What the failed
    // fan-out wrote is rolled back with it, and the rows due behind it are still delivered.
    [Fact]
    public async Task Failed_notification_is_retried_and_holds_up_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, _) = await EnqueueAsync(0);
        var notification = await EnqueueNotificationAsync(accountId);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var (_, emails) = await EnqueueAsync(1);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await Outbox(scope, Factory.Email, fanOut: FailingFanOut(scope)).RetryDueAsync(ct);
        }

        Assert.NotNull(Assert.Single(await RowsAsync(emails)).SentAt);
        var failed = Assert.Single(await RowsAsync([notification]));
        Assert.Null(failed.SentAt);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(Factory.Clock.UtcNow + OutboxQueue.Backoff[0], failed.NextAttemptAt);
        Assert.Equal(0, await NotificationCountAsync(notification));

        // Not due again before the backoff has passed; then delivered once.
        await RetryAsync();
        Assert.Equal(0, await NotificationCountAsync(notification));
        Factory.Clock.Advance(OutboxQueue.Backoff[0]);
        await RetryAsync();

        Assert.NotNull(Assert.Single(await RowsAsync([notification])).SentAt);
        Assert.Equal(1, await NotificationCountAsync(notification));
    }

    [Fact]
    public async Task Last_failed_notification_attempt_marks_the_row_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, _) = await EnqueueAsync(0);
        var notification = await EnqueueNotificationAsync(accountId);

        for (var attempt = 1; attempt <= OutboxQueue.MaximumAttempts; attempt++)
        {
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                await Outbox(scope, Factory.Email, fanOut: FailingFanOut(scope)).RetryDueAsync(ct);
            }

            if (attempt < OutboxQueue.MaximumAttempts)
            {
                Factory.Clock.Advance(OutboxQueue.Backoff[attempt - 1]);
            }
        }

        var row = Assert.Single(await RowsAsync([notification]));
        Assert.Equal(OutboxQueue.MaximumAttempts, row.Attempts);
        Assert.Equal(Factory.Clock.UtcNow, row.FailedAt);
        Assert.Null(row.SentAt);

        // Failed is final.
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        await RetryAsync();
        Assert.Equal(0, await NotificationCountAsync(notification));

        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        var log = Assert.Single(captured, item => item.Signal == "log" && Equals(item.Field("eventName"), "OutboxNotificationFailed"));
        Assert.Equal(accountId, log.Field("attribute:AccountId"));
    }

    private static RegistrationRequest Request(AccountDraft account) =>
        new(account.Email, account.Password, account.DateOfBirth, "nb", TermsVersion: 1, PrivacyVersion: 1);

    private static NotificationFanOut FailingFanOut(AsyncServiceScope scope) =>
        new(new FailingNotificationStore(scope.ServiceProvider.GetRequiredService<INotificationStore>()));

    private OutboxQueue Outbox(
        AsyncServiceScope scope, IEmailSender sender, IOutboxStore? store = null, NotificationFanOut? fanOut = null) =>
        new(store ?? scope.ServiceProvider.GetRequiredService<IOutboxStore>(), sender,
            fanOut ?? scope.ServiceProvider.GetRequiredService<NotificationFanOut>(), Factory.Clock,
            scope.ServiceProvider.GetRequiredService<ILogger<OutboxQueue>>());

    // A notification event for the account, naming the account itself, due now and not delivered.
    private async Task<Guid> EnqueueNotificationAsync(Guid accountId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var id = scope.ServiceProvider.GetRequiredService<OutboxQueue>()
            .Enqueue(new NotificationEvent(NotificationKind.InviteReceived, accountId, accountId, Guid.NewGuid()));
        await scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    // The in-app notifications delivered by this outbox row.
    private async Task<int> NotificationCountAsync(Guid outboxId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Notifications
            .CountAsync(row => row.Id == outboxId, TestContext.Current.CancellationToken);
    }

    // The sweeper's step, as the host wires it (the recording sender).
    private async Task RetryAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxQueue>().RetryDueAsync(TestContext.Current.CancellationToken);
    }

    private async Task<AccountDraft> RegisterAsync()
    {
        var account = Factory.Data.Account();
        using var response = await Factory.CreateClient().PostAsJsonAsync(
            "/api/climber/accounts", Request(account), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return account;
    }

    // Registers an account (its confirmation is delivered at once), then writes count more emails to
    // it, due now. Returns the account's ID and the new rows' IDs.
    private async Task<(Guid AccountId, Guid[] Rows)> EnqueueAsync(int count)
    {
        var ct = TestContext.Current.CancellationToken;
        var account = await RegisterAsync();
        Factory.Email.Clear();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>();
        var accountId = await db.Accounts.Where(row => row.Email == account.Email).Select(row => row.Id).SingleAsync(ct);
        var outbox = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var rows = Enumerable.Range(0, count)
            .Select(i => outbox.Enqueue(accountId, new EmailMessage(account.Email, $"Probe {i}", "A probe message.")))
            .ToArray();
        await db.SaveChangesAsync(ct);
        return (accountId, rows);
    }

    // Every outbox row, or those with these IDs.
    private async Task<IReadOnlyList<OutboxRow>> RowsAsync(Guid[]? ids = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var rows = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Outbox.AsNoTracking();
        return await (ids is null ? rows : rows.Where(row => ids.Contains(row.Id))).ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class FixedPassword : ISmtpPasswordSource
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("tem-key-for-tests");
    }

    // The real store, failing at one point as the database would.
    public sealed class BrokenStore(IOutboxStore inner, BrokenStore.Fault fault) : IOutboxStore
    {
        public enum Fault
        {
            Claim,
            MarkSent,
        }

        public void Add(OutboxItem item, DateTimeOffset now) => inner.Add(item, now);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);

        public async Task<IOutboxClaim?> ClaimNextAsync(DateTimeOffset now, Guid? id, CancellationToken cancellationToken)
        {
            if (fault == Fault.Claim)
            {
                throw new InvalidOperationException("The database is unavailable.");
            }

            return await inner.ClaimNextAsync(now, id, cancellationToken) is { } claim ? new BrokenClaim(claim) : null;
        }

        private sealed class BrokenClaim(IOutboxClaim inner) : IOutboxClaim
        {
            public OutboxItem Item => inner.Item;

            public int Attempts => inner.Attempts;

            public Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
                inner.RunAsync(work, cancellationToken);

            public Task MarkSentAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The database is unavailable.");

            public Task MarkRetryAsync(int attempts, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
                inner.MarkRetryAsync(attempts, nextAttemptAt, cancellationToken);

            public Task MarkFailedAsync(int attempts, DateTimeOffset now, CancellationToken cancellationToken) =>
                inner.MarkFailedAsync(attempts, now, cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    // The real store, which writes the notification and then fails, as a later statement of the fan-out would.
    private sealed class FailingNotificationStore(INotificationStore inner) : INotificationStore
    {
        public async Task AddInAppAsync(
            Guid id, NotificationEvent notification, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            await inner.AddInAppAsync(id, notification, occurredAt, cancellationToken);
            throw new InvalidOperationException("The notification could not be delivered.");
        }

        public Task<IReadOnlyList<InAppNotification>> ListAsync(Guid accountId, int size, CancellationToken cancellationToken) =>
            inner.ListAsync(accountId, size, cancellationToken);

        public Task<bool> MarkSeenAsync(Guid accountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
            inner.MarkSeenAsync(accountId, id, now, cancellationToken);
    }

    private sealed class FailingSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The email could not be sent.");
    }

    // Takes a little time per message, so the deliverers overlap.
    private sealed class SlowSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> sent = new();

        public IReadOnlyList<EmailMessage> Sent => sent.ToArray();

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            sent.Enqueue(message);
        }
    }
}

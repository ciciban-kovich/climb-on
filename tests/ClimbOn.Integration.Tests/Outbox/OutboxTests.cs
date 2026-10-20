using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Outbox;
using ClimbOn.Infrastructure.Email;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Integration.Tests.Email;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClimbOn.Integration.Tests.Outbox;

// Design §4.1. Rows are written through EmailOutbox below the API, and delivered by the step the
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
        Assert.Equal(Factory.Clock.UtcNow + EmailOutbox.Backoff[0], failed.NextAttemptAt);

        // Not due again before the backoff has passed.
        await RetryAsync();
        Assert.Empty(Factory.Email.Sent);

        Factory.Clock.Advance(EmailOutbox.Backoff[0]);
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

        for (var attempt = 1; attempt <= EmailOutbox.MaximumAttempts; attempt++)
        {
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                await Outbox(scope, new FailingSender()).RetryDueAsync(ct);
            }

            if (attempt < EmailOutbox.MaximumAttempts)
            {
                Factory.Clock.Advance(EmailOutbox.Backoff[attempt - 1]);
            }
        }

        var row = Assert.Single(await RowsAsync(rows));
        Assert.Equal(EmailOutbox.MaximumAttempts, row.Attempts);
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
        Assert.Equal(Factory.Clock.UtcNow + EmailOutbox.Backoff[0], row.NextAttemptAt);
        Assert.Null(row.SentAt);
        Assert.Null(row.FailedAt);
        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        Assert.Contains(captured, item => item.Signal == "log" && Equals(item.Field("eventName"), "EmailSendFailed")
            && Equals(item.Field("attribute:SmtpStatusCode"), 550));
    }

    private static RegistrationRequest Request(AccountDraft account) =>
        new(account.Email, account.Password, account.DateOfBirth, "nb", TermsVersion: 1, PrivacyVersion: 1);

    private EmailOutbox Outbox(AsyncServiceScope scope, IEmailSender sender) =>
        new(scope.ServiceProvider.GetRequiredService<IOutboxStore>(), sender, Factory.Clock);

    // The sweeper's step, as the host wires it (the recording sender).
    private async Task RetryAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailOutbox>().RetryDueAsync(TestContext.Current.CancellationToken);
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
        var outbox = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
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

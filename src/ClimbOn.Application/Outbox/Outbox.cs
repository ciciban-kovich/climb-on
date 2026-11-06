using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Application.Outbox;

// A row of the outbox: an email to send, or a notification event to fan out (design §4.1). AccountId is the
// recipient's.
public abstract record OutboxItem(Guid Id, Guid AccountId);

public sealed record OutboxEmail(Guid Id, Guid AccountId, EmailMessage Message) : OutboxItem(Id, AccountId);

// OccurredAt is when the event happened, the time its notifications carry, whenever they are delivered.
public sealed record OutboxNotification(Guid Id, NotificationEvent Event, DateTimeOffset OccurredAt)
    : OutboxItem(Id, Event.RecipientId);

public interface IOutboxStore
{
    // Saved with the request's other changes, in one transaction (IAccountStore.AddAsync).
    void Add(OutboxItem item, DateTimeOffset now);

    // Commits the rows added, for a request whose only change is an email (OutboxQueue.EnqueueAsync).
    Task SaveAsync(CancellationToken cancellationToken);

    // Opens a transaction and locks the oldest due row (FOR UPDATE SKIP LOCKED), or the row with
    // this id when it is due; null when there is none. Disposing an unmarked claim rolls back.
    Task<IOutboxClaim?> ClaimNextAsync(DateTimeOffset now, Guid? id, CancellationToken cancellationToken);
}

// A claimed row. Each mark commits the claim's transaction.
public interface IOutboxClaim : IAsyncDisposable
{
    OutboxItem Item { get; }

    // Failed attempts so far.
    int Attempts { get; }

    Task MarkSentAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task MarkRetryAsync(int attempts, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);

    // The last attempt failed; the store logs it by account ID only (C32).
    Task MarkFailedAsync(int attempts, DateTimeOffset now, CancellationToken cancellationToken);
}

// Design §4.1: a row is written to the outbox in the same transaction as the state change, delivered after commit,
// and what remains is retried by the sweeper (RetryDueAsync). An email that fails is retried with backoff. A
// notification event is fanned out inside the claim's transaction, so its channels' rows commit with the mark.
public sealed partial class OutboxQueue(
    IOutboxStore store, IEmailSender sender, NotificationFanOut notifications, IClock clock, ILogger<OutboxQueue> logger)
{
    // The wait after each failed attempt; the attempt after the last of these is the last one.
    public static readonly IReadOnlyList<TimeSpan> Backoff =
        [TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromHours(12)];

    public static int MaximumAttempts => Backoff.Count + 1;

    public Guid Enqueue(Guid accountId, EmailMessage message)
    {
        var now = clock.UtcNow;
        var email = new OutboxEmail(Guid.CreateVersion7(now), accountId, message);
        store.Add(email, now);
        return email.Id;
    }

    // S69: saved with the request's other changes, as an email is.
    public Guid Enqueue(NotificationEvent notification)
    {
        var now = clock.UtcNow;
        var item = new OutboxNotification(Guid.CreateVersion7(now), notification, now);
        store.Add(item, now);
        return item.Id;
    }

    // Enqueue for a request that changes nothing else: the row is committed on its own.
    public async Task<Guid> EnqueueAsync(Guid accountId, EmailMessage message, CancellationToken cancellationToken)
    {
        var id = Enqueue(accountId, message);
        await store.SaveAsync(cancellationToken);
        return id;
    }

    // After commit: one attempt at this row. Any failure, the store's included, leaves it to the sweeper,
    // so the request that committed the row still succeeds. Logged by exception type only (C32).
    public async Task DeliverAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await DeliverDueAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogDeliveryFailed(id, exception.GetType().Name);
        }
    }

    // The sweeper's step: every row that is due now.
    public Task RetryDueAsync(CancellationToken cancellationToken) => DeliverDueAsync(null, cancellationToken);

    private async Task DeliverDueAsync(Guid? id, CancellationToken cancellationToken)
    {
        // Every row handled leaves the due set (sent, failed or due later), so this ends.
        while (await store.ClaimNextAsync(clock.UtcNow, id, cancellationToken) is { } claim)
        {
            await using (claim)
            {
                await AttemptAsync(claim, cancellationToken);
            }

            if (id is not null)
            {
                return;
            }
        }
    }

    private async Task AttemptAsync(IOutboxClaim claim, CancellationToken cancellationToken)
    {
        switch (claim.Item)
        {
            case OutboxEmail email:
                await SendAsync(claim, email, cancellationToken);
                break;
            case OutboxNotification notification:
                // A failure here throws with the claim unmarked, so its transaction rolls back and the row stays due.
                await notifications.DeliverAsync(notification.Id, notification.Event, notification.OccurredAt, cancellationToken);
                await claim.MarkSentAsync(clock.UtcNow, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unknown outbox item {claim.Item.GetType().Name}.");
        }
    }

    private async Task SendAsync(IOutboxClaim claim, OutboxEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(email.Message, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The sender logs and counts its own failures (design §4.4).
            var attempts = claim.Attempts + 1;
            var now = clock.UtcNow;
            if (attempts >= MaximumAttempts)
            {
                await claim.MarkFailedAsync(attempts, now, cancellationToken);
            }
            else
            {
                await claim.MarkRetryAsync(attempts, now + Backoff[attempts - 1], cancellationToken);
            }

            return;
        }

        await claim.MarkSentAsync(clock.UtcNow, cancellationToken);
    }

    [LoggerMessage(
        EventName = "OutboxDeliveryFailed",
        Level = LogLevel.Warning,
        Message = "Delivering outbox row {OutboxId} after commit failed: {ExceptionType}")]
    private partial void LogDeliveryFailed(Guid outboxId, string exceptionType);
}

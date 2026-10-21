using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Infrastructure.Persistence.Outbox;

// Design §4.1: a row is claimed with FOR UPDATE SKIP LOCKED and marked in the same transaction, so it
// is delivered by one worker at a time.
public sealed partial class OutboxStore(ClimbOnDbContext db, ILogger<OutboxStore> logger) : IOutboxStore
{
    public void Add(OutboxEmail email, DateTimeOffset now) =>
        db.Outbox.Add(new OutboxRow
        {
            Id = email.Id,
            AccountId = email.AccountId,
            Recipient = email.Message.To,
            Subject = email.Message.Subject,
            Body = email.Message.Body,
            CreatedAt = now,
            NextAttemptAt = now,
        });

    public async Task<IOutboxClaim?> ClaimNextAsync(DateTimeOffset now, Guid? id, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var due = id is { } only
                ? db.Outbox.FromSql($"""
                    SELECT * FROM outbox
                    WHERE id = {only} AND sent_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                    FOR UPDATE SKIP LOCKED
                    """)
                : db.Outbox.FromSql($"""
                    SELECT * FROM outbox
                    WHERE sent_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                    ORDER BY next_attempt_at, id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """);
            if ((await due.AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault() is not { } row)
            {
                await transaction.DisposeAsync();
                return null;
            }

            return new Claim(this, db, transaction, row);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    [LoggerMessage(
        EventName = "OutboxEmailFailed",
        Level = LogLevel.Error,
        Message = "Outbox email {OutboxId} for account {AccountId} failed after {Attempts} attempts")]
    private partial void LogFailed(Guid outboxId, Guid accountId, int attempts);

    private sealed class Claim(OutboxStore store, ClimbOnDbContext db, IDbContextTransaction transaction, OutboxRow row) : IOutboxClaim
    {
        public OutboxEmail Email { get; } =
            new(row.Id, row.AccountId, new EmailMessage(row.Recipient!, row.Subject!, row.Body!));

        public int Attempts => row.Attempts;

        public Task MarkSentAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            CommitAsync(
                setters => setters
                    .SetProperty(outbox => outbox.SentAt, now)
                    .SetProperty(outbox => outbox.Recipient, (string?)null)
                    .SetProperty(outbox => outbox.Subject, (string?)null)
                    .SetProperty(outbox => outbox.Body, (string?)null),
                cancellationToken);

        public Task MarkRetryAsync(int attempts, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
            CommitAsync(
                setters => setters
                    .SetProperty(outbox => outbox.Attempts, attempts)
                    .SetProperty(outbox => outbox.NextAttemptAt, nextAttemptAt),
                cancellationToken);

        public async Task MarkFailedAsync(int attempts, DateTimeOffset now, CancellationToken cancellationToken)
        {
            await CommitAsync(
                setters => setters
                    .SetProperty(outbox => outbox.Attempts, attempts)
                    .SetProperty(outbox => outbox.FailedAt, now)
                    .SetProperty(outbox => outbox.Recipient, (string?)null)
                    .SetProperty(outbox => outbox.Subject, (string?)null)
                    .SetProperty(outbox => outbox.Body, (string?)null),
                cancellationToken);
            store.LogFailed(row.Id, row.AccountId, attempts);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();

        private async Task CommitAsync(Action<UpdateSettersBuilder<OutboxRow>> setters, CancellationToken cancellationToken)
        {
            await db.Outbox.Where(outbox => outbox.Id == row.Id).ExecuteUpdateAsync(setters, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}

namespace ClimbOn.Infrastructure.Persistence.Outbox;

// A row of outbox. Recipient, subject and body are cleared once the row is sent or has failed, so a
// link token in the body is kept only while it may still be delivered.
public sealed class OutboxRow
{
    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public string? Recipient { get; init; }

    public string? Subject { get; init; }

    public string? Body { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // Failed attempts so far.
    public int Attempts { get; init; }

    public required DateTimeOffset NextAttemptAt { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    public DateTimeOffset? FailedAt { get; init; }
}

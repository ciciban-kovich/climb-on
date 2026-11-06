namespace ClimbOn.Infrastructure.Persistence.Outbox;

// A row of outbox: an email, or a notification event (Kind). An email's recipient, subject and body are cleared once
// the row is sent or has failed, so a link token in the body is kept only while it may still be delivered. A
// notification event holds Event, ActorId and SubjectId instead, and no message content (S74).
public sealed class OutboxRow
{
    public const string EmailKind = "email";
    public const string NotificationKind = "notification";

    public required Guid Id { get; init; }

    public required Guid AccountId { get; init; }

    public required string Kind { get; init; }

    // A wire name in NotificationNames.
    public string? Event { get; init; }

    public Guid? ActorId { get; init; }

    public Guid? SubjectId { get; init; }

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

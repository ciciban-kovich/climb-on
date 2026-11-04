using ClimbOn.Domain.Chat;

namespace ClimbOn.Infrastructure.Persistence.Chat;

// A row of messages. Position is the order rows were stored in, which sent_at alone cannot give for two messages
// sent at the same instant; it is never shown.
public sealed class MessageRow
{
    public required Guid Id { get; init; }

    public long Position { get; init; }

    public required Guid ConnectionId { get; init; }

    public required Guid SenderId { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset SentAt { get; init; }

    public Message ToMessage() => new(Id, ConnectionId, SenderId, Text, SentAt);
}

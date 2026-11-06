using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Invites;
using ClimbOn.Application.Notifications;
using ClimbOn.Application.Outbox;
using ClimbOn.Domain.Chat;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Application.Chat;

// Sending and reading the messages of a connection (S9, S115). Only its two climbers can do either; to anyone else
// the connection does not exist.
public sealed class Messages(IChatStore chat, IConnectionStore connections, OutboxQueue outbox, IClock clock)
{
    public const string ConnectionNotFoundCode = "connection_not_found";
    public const string ConnectionNotActiveCode = "connection_not_active";
    public const string MessageCursorInvalidCode = "message_cursor_invalid";

    // S118.
    public const int PageSize = 10;

    public static IReadOnlyList<string> Codes { get; } =
        [ConnectionNotFoundCode, ConnectionNotActiveCode, MessageCursorInvalidCode, .. Message.Codes];

    // S9: only within an active connection. The message notifies the other climber (S69.3), without its content (S74).
    public async Task<Message> SendAsync(Guid senderId, Guid connectionId, string? text, CancellationToken cancellationToken)
    {
        var message = Message.Write(Guid.NewGuid(), connectionId, senderId, text, clock.UtcNow);
        var connection = await FindAsync(senderId, connectionId, cancellationToken);

        // Enqueued first, so that storing the message saves it in the same transaction, or drops it with the message.
        var notified = outbox.Enqueue(
            new NotificationEvent(NotificationKind.NewMessage, connection.PartnerId, senderId, connectionId));
        if (!await chat.AddIfActiveAsync(message, cancellationToken))
        {
            throw new DomainError(ConnectionNotActiveCode, DomainErrorKind.Conflict);
        }

        await outbox.DeliverAsync(notified, cancellationToken);
        return message;
    }

    // S9: readable in every state of the connection. S118: a page of the newest messages, or with before (a message
    // ID) of the ones before it. The connection is checked first, so to anyone else it does not exist whatever the
    // cursor; a cursor that is not a message of this connection is refused like a malformed one, so the reply never
    // shows whether a message of another connection exists.
    public async Task<MessagePage> ListAsync(
        Guid viewerId, Guid connectionId, string? before, CancellationToken cancellationToken)
    {
        await FindAsync(viewerId, connectionId, cancellationToken);
        Guid? cursor = null;
        if (before is not null)
        {
            cursor = Guid.TryParse(before, out var id) ? id : throw CursorInvalid();
        }

        return await chat.ListMessagesAsync(connectionId, cursor, PageSize, cancellationToken) ?? throw CursorInvalid();
    }

    private static DomainError CursorInvalid() => new(MessageCursorInvalidCode);

    private async Task<Connection> FindAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken) =>
        await connections.FindAsync(accountId, connectionId, cancellationToken)
            ?? throw new DomainError(ConnectionNotFoundCode, DomainErrorKind.NotFound);
}

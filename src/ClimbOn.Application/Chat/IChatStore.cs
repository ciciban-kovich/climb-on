using ClimbOn.Domain.Chat;

namespace ClimbOn.Application.Chat;

public interface IChatStore
{
    // Up to size messages of the connection, in the order they were stored: the newest ones, or with before the ones
    // stored just before that message. Null when before is not a message of this connection.
    Task<MessagePage?> ListMessagesAsync(Guid connectionId, Guid? before, int size, CancellationToken cancellationToken);

    // Stores the message if its connection is active, holding the connection row FOR SHARE to commit so that no
    // state change of the connection commits between the check and the insert. False when it is not active.
    Task<bool> AddIfActiveAsync(Message message, CancellationToken cancellationToken);
}

// HasOlder is whether the connection holds messages stored before the first one of the page.
public sealed record MessagePage(IReadOnlyList<Message> Messages, bool HasOlder);

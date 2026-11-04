using ClimbOn.Domain.Chat;

namespace ClimbOn.Application.Chat;

public interface IChatStore
{
    // Every message of the connection, in the order they were stored.
    Task<IReadOnlyList<Message>> ListMessagesAsync(Guid connectionId, CancellationToken cancellationToken);

    // Stores the message if its connection is active, holding the connection row FOR SHARE to commit so that no
    // state change of the connection commits between the check and the insert. False when it is not active.
    Task<bool> AddIfActiveAsync(Message message, CancellationToken cancellationToken);
}

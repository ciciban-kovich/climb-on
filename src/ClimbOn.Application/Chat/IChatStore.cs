using ClimbOn.Domain.Chat;

namespace ClimbOn.Application.Chat;

// Specs glossary; pausing and ending arrive in M2.
public enum ConnectionState
{
    Active,
    Paused,
    Ended,
}

// A connection as one of its two climbers sees it.
public sealed record Connection(Guid Id, Guid PartnerId, ConnectionState State, DateTimeOffset CreatedAt);

public interface IChatStore
{
    // Every connection the account is in, in any state.
    Task<IReadOnlyList<Connection>> ListConnectionsAsync(Guid accountId, CancellationToken cancellationToken);

    // Null when there is no such connection or the account is not in it.
    Task<Connection?> FindConnectionAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken);

    // Every message of the connection, in the order they were stored.
    Task<IReadOnlyList<Message>> ListMessagesAsync(Guid connectionId, CancellationToken cancellationToken);

    // Stores the message if its connection is active, holding the connection row FOR SHARE to commit so that no
    // state change of the connection commits between the check and the insert. False when it is not active.
    Task<bool> AddIfActiveAsync(Message message, CancellationToken cancellationToken);
}

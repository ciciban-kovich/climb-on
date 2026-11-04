using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// A connection as one of its two climbers sees it.
public sealed record Connection(Guid Id, Guid PartnerId, ConnectionState State, DateTimeOffset CreatedAt);

public interface IConnectionStore
{
    // Every connection the account is in, in any state.
    Task<IReadOnlyList<Connection>> ListAsync(Guid accountId, CancellationToken cancellationToken);

    // Null when there is no such connection or the account is not in it.
    Task<Connection?> FindAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken);
}

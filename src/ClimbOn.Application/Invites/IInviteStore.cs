using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// Which locks a pair action takes (design §2 'Concurrency for pair and count rules').
public enum PairAction
{
    // Sending, including the S34 path: the actor's row FOR NO KEY UPDATE (S31), the other's FOR SHARE.
    Invite,

    // Accepting or declining: both rows FOR SHARE.
    Answer,
}

// An account as read under its row lock. Accounts are active until M2 adds suspension and deletion.
public sealed record PairAccount(Guid Id, bool EmailConfirmed);

// One transaction holding both account rows and the pair lock, as LockPairAsync took them. Disposing it uncommitted
// rolls it back.
public interface IPairTransaction : IAsyncDisposable
{
    // Null when there is no such account.
    PairAccount? Actor { get; }

    PairAccount? Other { get; }

    // Every invite between the two, in either direction, as stored once the timeouts already due are written.
    IReadOnlyList<Invite> Invites { get; }

    // An active or paused connection between the two exists.
    bool Connected { get; }

    // S31: the actor's outgoing invites pending at now.
    Task<int> CountPendingSentAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task AddAsync(Invite invite, CancellationToken cancellationToken);

    // Writes the invite's new state: declined, or accepted together with the active connection it creates.
    Task DeclineAsync(Invite declined, CancellationToken cancellationToken);

    Task AcceptAsync(Invite accepted, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IInviteStore
{
    // Opens a READ COMMITTED transaction and locks, in this order: both account rows in ascending ID order, each
    // once in the mode action needs; the pair's advisory lock; for Invite, both profile rows FOR SHARE, so that the
    // candidate check reads each profile whole. Then writes the pair's timeouts due at now and reads the pair.
    Task<IPairTransaction> LockPairAsync(
        Guid actorId, Guid otherId, PairAction action, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Invite?> FindAsync(Guid inviteId, CancellationToken cancellationToken);

    // Every invite the account sent, and those it received that are still pending at now.
    Task<IReadOnlyList<Invite>> ListAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken);

    // S23: when the account acknowledged the safety notice; null when it has not.
    Task<DateTimeOffset?> FindSafetyAcknowledgementAsync(Guid accountId, CancellationToken cancellationToken);

    // Stores the acknowledgement at now unless there is one, and returns the stored time.
    Task<DateTimeOffset> AcknowledgeSafetyNoticeAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken);
}

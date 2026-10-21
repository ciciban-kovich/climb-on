using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Tokens;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// The account a live session belongs to.
public sealed record SessionOwner(Guid AccountId, Language Language);

// A session resumed by a request: whose it is, and when it now expires (S82).
public sealed record ClimberSession(Guid AccountId, Language Language, DateTimeOffset ExpiresAt);

// Sessions are stored by the SHA-256 hash of their token, never the token (design §3).
public interface ISessionStore
{
    // Inserts the session and sets the account's last activity to now, in one transaction.
    Task StartAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    // Design §2 'Request pipeline': when the session was last used after usedAfter, sets the account's last
    // activity and then the session's last use to now, in a transaction of its own that has committed when
    // this returns. Null when there is no such session.
    Task<SessionOwner?> TouchAsync(byte[] tokenHash, DateTimeOffset now, DateTimeOffset usedAfter, CancellationToken cancellationToken);

    Task EndAsync(byte[] tokenHash, CancellationToken cancellationToken);
}

// S82: a session lasts until 30 days after its last use; logging out ends it.
public sealed class Sessions(ISessionStore store, IClock clock)
{
    // Every authenticated request: null when the token names no live session.
    public async Task<ClimberSession?> ResumeAsync(string token, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return await store.TouchAsync(LinkTokens.Hash(token), now, SessionPolicy.LiveIfUsedAfter(now), cancellationToken) is { } owner
            ? new ClimberSession(owner.AccountId, owner.Language, SessionPolicy.ExpiresAt(now))
            : null;
    }

    public Task EndAsync(string token, CancellationToken cancellationToken) =>
        store.EndAsync(LinkTokens.Hash(token), cancellationToken);
}

namespace ClimbOn.Domain.Accounts;

// S82: a session lives until 30 days after its last use.
public static class SessionPolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    // When a session used at lastUsedAt expires.
    public static DateTimeOffset ExpiresAt(DateTimeOffset lastUsedAt) => lastUsedAt + Lifetime;

    // A session is live at now only if it was last used after this time.
    public static DateTimeOffset LiveIfUsedAfter(DateTimeOffset now) => now - Lifetime;
}

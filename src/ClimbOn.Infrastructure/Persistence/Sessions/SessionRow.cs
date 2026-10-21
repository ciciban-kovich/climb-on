namespace ClimbOn.Infrastructure.Persistence.Sessions;

// A row of sessions: the SHA-256 hash of a session token, never the token (design §3).
public sealed class SessionRow
{
    public required byte[] TokenHash { get; init; }

    public required Guid AccountId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // S82: the session expires 30 days after this.
    public required DateTimeOffset LastUsedAt { get; set; }
}

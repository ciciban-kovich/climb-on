namespace ClimbOn.Infrastructure.Persistence.Outbox;

// A row of link_tokens: the SHA-256 hash of a token, never the token (design §3).
public sealed class LinkTokenRow
{
    public required byte[] TokenHash { get; init; }

    public required Guid AccountId { get; init; }

    // A LinkPurposes name.
    public required string Purpose { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }
}

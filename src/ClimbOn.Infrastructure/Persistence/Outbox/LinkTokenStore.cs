using ClimbOn.Application.Tokens;

namespace ClimbOn.Infrastructure.Persistence.Outbox;

public static class LinkPurposes
{
    public const string EmailConfirmation = "email_confirmation";

    public static string Name(this LinkPurpose purpose) => purpose switch
    {
        LinkPurpose.EmailConfirmation => EmailConfirmation,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };
}

public sealed class LinkTokenStore(ClimbOnDbContext db) : ILinkTokenStore
{
    public void Add(LinkToken token) =>
        db.LinkTokens.Add(new LinkTokenRow
        {
            TokenHash = token.Hash,
            AccountId = token.AccountId,
            Purpose = token.Purpose.Name(),
            CreatedAt = token.CreatedAt,
        });
}

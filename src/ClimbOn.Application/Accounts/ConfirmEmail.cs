using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Tokens;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Application.Accounts;

// S49: the link sent at registration confirms the address. A token works once and does not expire.
public sealed class ConfirmEmail(IAccountStore accounts, IClock clock)
{
    // Unknown and already used tokens get the same answer.
    public const string LinkInvalidCode = "link_invalid";

    public async Task RunAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token)
            || !await accounts.ConfirmEmailAsync(LinkTokens.Hash(token), clock.UtcNow, cancellationToken))
        {
            throw new DomainError(LinkInvalidCode);
        }
    }
}

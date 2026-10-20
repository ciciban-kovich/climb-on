using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Legal;
using ClimbOn.Application.Outbox;
using ClimbOn.Application.Tokens;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// S1 (email), S27, S28, S47, S49, S79, S104, S111: registration with email and password.
public sealed class Register(
    IAccountStore accounts,
    ILegalDocumentStore legalDocuments,
    IPasswordHasher hasher,
    ILinkTokenStore linkTokens,
    EmailOutbox outbox,
    IEmailContent emails,
    IClock clock)
{
    // Returns the language stored for the new account (S104); throws DomainError when a rule refuses it.
    public async Task<Language> RunAsync(EmailRegistration registration, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var current = await legalDocuments.CurrentVersionsAsync(cancellationToken);
        var account = Account.Register(Guid.CreateVersion7(now), registration, current, now, hasher.Hash);

        // S49: the confirmation link, committed with the account.
        var token = LinkTokens.Create();
        linkTokens.Add(new LinkToken(LinkTokens.Hash(token), account.Id, LinkPurpose.EmailConfirmation, now));
        var email = outbox.Enqueue(account.Id, emails.EmailConfirmation(account.Email, account.Language, token));
        await accounts.AddAsync(account, cancellationToken);

        await outbox.DeliverAsync(email, cancellationToken);
        return account.Language;
    }
}

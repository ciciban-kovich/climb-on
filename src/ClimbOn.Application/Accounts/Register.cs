using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Legal;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// S1 (email), S27, S28, S47, S79, S104, S111: registration with email and password.
public sealed class Register(IAccountStore accounts, ILegalDocumentStore legalDocuments, IPasswordHasher hasher, IClock clock)
{
    // Returns the language stored for the new account (S104); throws DomainError when a rule refuses it.
    public async Task<Language> RunAsync(EmailRegistration registration, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var current = await legalDocuments.CurrentVersionsAsync(cancellationToken);
        var account = Account.Register(Guid.CreateVersion7(now), registration, current, now, hasher.Hash);
        await accounts.AddAsync(account, cancellationToken);
        return account.Language;
    }
}

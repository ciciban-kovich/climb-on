using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Legal;

namespace ClimbOn.Infrastructure.Persistence.Accounts;

public sealed class AccountStore(ClimbOnDbContext db) : IAccountStore
{
    public async Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        db.Accounts.Add(new AccountRow
        {
            Id = account.Id,
            Email = account.Email,
            PasswordHash = account.PasswordHash,
            DateOfBirth = account.DateOfBirth,
            Language = account.Language.Tag(),
            RegisteredAt = account.RegisteredAt,
        });
        db.LegalAcknowledgements.AddRange(account.Acknowledgements.Select(acknowledgement => new LegalAcknowledgementRow
        {
            AccountId = account.Id,
            Document = acknowledgement.Document.Name(),
            Version = acknowledgement.Version,
            AcknowledgedAt = acknowledgement.At,
        }));

        // One SaveChanges is one transaction.
        await db.SaveChangesAsync(cancellationToken);
    }
}

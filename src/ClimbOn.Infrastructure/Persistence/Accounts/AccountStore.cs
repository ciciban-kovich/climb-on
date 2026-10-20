using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Legal;
using ClimbOn.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

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

    // One statement, so a token is used once even by concurrent requests: the second waits for the
    // first's row lock and then finds the token used.
    public async Task<bool> ConfirmEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            WITH used AS (
                UPDATE link_tokens SET used_at = {now}
                WHERE token_hash = {tokenHash} AND purpose = {LinkPurposes.EmailConfirmation} AND used_at IS NULL
                RETURNING account_id)
            UPDATE accounts SET email_confirmed_at = COALESCE(accounts.email_confirmed_at, {now})
            FROM used
            WHERE accounts.id = used.account_id
            """,
            cancellationToken) == 1;
}

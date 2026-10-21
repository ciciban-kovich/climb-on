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
            LastActivityAt = account.RegisteredAt,
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

    // Addresses are stored trimmed (Account.Register). Until task 12's unique index, an address may
    // belong to more than one account; the first registered is the one that logs in.
    public Task<AccountCredentials?> FindCredentialsAsync(string email, CancellationToken cancellationToken)
    {
        var normalised = email.Trim().ToLowerInvariant();
        return db.Accounts.AsNoTracking()
            .Where(account => account.Email.ToLower() == normalised)
            .OrderBy(account => account.RegisteredAt)
            .Select(account => new AccountCredentials(account.Id, account.PasswordHash))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OwnAccount?> FindOwnAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(account => account.Id == id)
            .Select(account => new { account.Email, account.Language, account.EmailConfirmedAt })
            .FirstOrDefaultAsync(cancellationToken) is { } found
            ? new OwnAccount(found.Email, Languages.FromClientSetting(found.Language), found.EmailConfirmedAt is not null)
            : null;
}

using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Legal;
using ClimbOn.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClimbOn.Infrastructure.Persistence.Accounts;

public sealed class AccountStore(ClimbOnDbContext db) : IAccountStore
{
    public async Task<bool> AddAsync(Account account, CancellationToken cancellationToken)
    {
        db.Accounts.Add(new AccountRow
        {
            Id = account.Id,
            Email = account.Email,
            NormalisedEmail = account.NormalisedEmail,
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

        // One SaveChanges is one transaction. S84 is the unique index's to decide, so a registration
        // racing this one waits for it and then fails here.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ClimbOnDbContext.AccountEmailIndex,
        })
        {
            db.ChangeTracker.Clear();
            return false;
        }
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

    public Task<AccountCredentials?> FindCredentialsAsync(string normalisedEmail, CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking()
            .Where(account => account.NormalisedEmail == normalisedEmail)
            .Select(account => new AccountCredentials(account.Id, account.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<EmailOwner?> FindEmailOwnerAsync(string normalisedEmail, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(account => account.NormalisedEmail == normalisedEmail)
            .Select(account => new { account.Id, account.Email, account.Language })
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? new EmailOwner(found.Id, found.Email, Languages.FromClientSetting(found.Language))
            : null;

    public async Task<OwnAccount?> FindOwnAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(account => account.Id == id)
            .Select(account => new { account.Email, account.Language, account.EmailConfirmedAt })
            .FirstOrDefaultAsync(cancellationToken) is { } found
            ? new OwnAccount(found.Email, Languages.FromClientSetting(found.Language), found.EmailConfirmedAt is not null)
            : null;

    public Task SetLanguageAsync(Guid id, Language language, CancellationToken cancellationToken) =>
        db.Accounts.Where(account => account.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.Language, language.Tag()), cancellationToken);
}

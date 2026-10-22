using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Sessions;

// Every transaction here that writes an account row and its session rows writes the account row first
// (design §2 'Request pipeline').
public sealed class SessionStore(ClimbOnDbContext db) : ISessionStore
{
    public async Task StartAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Accounts.Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.LastActivityAt, now), cancellationToken);
        db.Sessions.Add(new SessionRow { TokenHash = tokenHash, AccountId = accountId, CreatedAt = now, LastUsedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SessionOwner?> TouchAsync(
        byte[] tokenHash, DateTimeOffset now, DateTimeOffset usedAfter, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var owner = await (
                from session in db.Sessions.AsNoTracking()
                join account in db.Accounts.AsNoTracking() on session.AccountId equals account.Id
                where session.TokenHash == tokenHash && session.LastUsedAt > usedAfter
                select new { account.Id, account.Language })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null)
        {
            return null;
        }

        await db.Accounts.Where(account => account.Id == owner.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.LastActivityAt, now), cancellationToken);

        // Checked again under the row lock: logging out may have ended the session since it was read.
        var touched = await db.Sessions.Where(session => session.TokenHash == tokenHash && session.LastUsedAt > usedAfter)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.LastUsedAt, now), cancellationToken);
        if (touched == 0)
        {
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return new SessionOwner(owner.Id, Languages.FromClientSetting(owner.Language));
    }

    public Task EndAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.Sessions.Where(session => session.TokenHash == tokenHash).ExecuteDeleteAsync(cancellationToken);
}

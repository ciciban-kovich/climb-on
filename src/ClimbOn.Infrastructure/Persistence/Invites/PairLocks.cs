using ClimbOn.Application.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Invites;

// Design §2 'Concurrency for pair and count rules': the row locks and the pair lock, in the caller's transaction.
internal static class PairLocks
{
    // The first key of every pair lock. The two-key advisory locks are a key space of their own, apart from the
    // one-key locks (such as a job's lock per database), so this only has to differ from other two-key users.
    private const int PairLockSpace = 19;

    // Ascending in PostgreSQL's uuid order, which compares the 16 bytes in the order the canonical text shows them,
    // as an ordinal comparison of that text does.
    public static (Guid Low, Guid High) Order(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString("D"), b.ToString("D")) < 0 ? (a, b) : (b, a);

    // Locks both account rows in ascending ID order, each once in its strongest mode and never upgraded: for an
    // invite the actor's FOR NO KEY UPDATE and the other's FOR SHARE, for an answer both FOR SHARE. Then the pair's
    // advisory lock, and for an invite both profile rows FOR SHARE. Returns each account as read under its lock.
    public static async Task<(PairAccount? Actor, PairAccount? Other)> LockAsync(
        ClimbOnDbContext db, Guid actorId, Guid otherId, PairAction action, CancellationToken cancellationToken)
    {
        var (low, high) = Order(actorId, otherId);
        var accounts = new Dictionary<Guid, PairAccount?>();
        foreach (var id in new[] { low, high })
        {
            var confirmed = action == PairAction.Invite && id == actorId
                ? await db.Database.SqlQuery<bool>(
                        $"""SELECT email_confirmed_at IS NOT NULL AS "Value" FROM accounts WHERE id = {id} FOR NO KEY UPDATE""")
                    .ToListAsync(cancellationToken)
                : await db.Database.SqlQuery<bool>(
                        $"""SELECT email_confirmed_at IS NOT NULL AS "Value" FROM accounts WHERE id = {id} FOR SHARE""")
                    .ToListAsync(cancellationToken);
            accounts[id] = confirmed.Count == 1 ? new PairAccount(id, confirmed[0]) : null;
        }

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({PairLockSpace}, hashtext({low.ToString("D")} || ':' || {high.ToString("D")}))",
            cancellationToken);

        // A profile save upserts its profile row first and holds it to commit (ProfileStore.SaveAsync), so with both
        // rows shared the candidate check's several reads of a profile all see one saved version, as the repeatable
        // read of the list does.
        if (action == PairAction.Invite)
        {
            await db.Database.SqlQuery<Guid>(
                    $"""SELECT account_id AS "Value" FROM profiles WHERE account_id IN ({low}, {high}) ORDER BY account_id FOR SHARE""")
                .ToListAsync(cancellationToken);
        }

        return (accounts[actorId], accounts[otherId]);
    }
}

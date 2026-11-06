using ClimbOn.Application.Invites;
using ClimbOn.Domain.Invites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClimbOn.Infrastructure.Persistence.Invites;

public sealed class InviteStore(ClimbOnDbContext db) : IInviteStore
{
    public async Task<IPairTransaction> LockPairAsync(
        Guid actorId, Guid otherId, PairAction action, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // READ COMMITTED (design §2): every statement after a lock wait sees what the holder committed.
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var (actor, other) = await PairLocks.LockAsync(db, actorId, otherId, action, cancellationToken);

            // The timeouts already due (S8), written as the sweeper writes them (design §4.2), so that a new invite
            // after one finds no stored pending invite in the way of the S32 index.
            await ExpireTimedOutAsync(
                db.Invites.Where(row => (row.SenderId == actorId && row.RecipientId == otherId)
                    || (row.SenderId == otherId && row.RecipientId == actorId)),
                now,
                cancellationToken);

            var invites = await db.Invites.AsNoTracking()
                .Where(row => (row.SenderId == actorId && row.RecipientId == otherId)
                    || (row.SenderId == otherId && row.RecipientId == actorId))
                .ToListAsync(cancellationToken);
            var (low, high) = PairLocks.Order(actorId, otherId);
            var connected = await db.Connections.AnyAsync(
                row => row.PairLow == low && row.PairHigh == high
                    && (row.State == InviteNames.ConnectionActive || row.State == InviteNames.ConnectionPaused),
                cancellationToken);
            return new PairTransaction(db, transaction, actorId, actor, other, [.. invites.Select(row => row.ToInvite())], connected);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    // Design §4.2: one transaction per invite, under its pair's lock, as LockPairAsync writes the timeouts, so that no
    // pair action finds an invite it read as pending already written expired.
    public async Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = now - Invite.Lifetime;
        var invites = await db.Invites.AsNoTracking()
            .Where(row => row.State == InviteNames.Pending && row.SentAt <= due)
            .Select(row => new { row.Id, row.SenderId, row.RecipientId })
            .ToListAsync(cancellationToken);
        var expired = 0;
        foreach (var invite in invites)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await PairLocks.LockPairOnlyAsync(db, invite.SenderId, invite.RecipientId, cancellationToken);
            expired += await ExpireTimedOutAsync(db.Invites.Where(row => row.Id == invite.Id), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return expired;
    }

    public async Task<Invite?> FindAsync(Guid inviteId, CancellationToken cancellationToken) =>
        (await db.Invites.AsNoTracking().SingleOrDefaultAsync(row => row.Id == inviteId, cancellationToken))?.ToInvite();

    public async Task<IReadOnlyList<Invite>> ListAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sentAfter = now - Invite.Lifetime;
        return (await db.Invites.AsNoTracking()
                .Where(row => row.SenderId == accountId
                    || (row.RecipientId == accountId && row.State == InviteNames.Pending && row.SentAt > sentAfter))
                .ToListAsync(cancellationToken))
            .Select(row => row.ToInvite())
            .ToList();
    }

    public async Task<DateTimeOffset?> FindSafetyAcknowledgementAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.SafetyAcknowledgements.AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => (DateTimeOffset?)row.AcknowledgedAt)
            .SingleOrDefaultAsync(cancellationToken);

    // One statement, so concurrent acknowledgements store one row, the first's.
    public async Task<DateTimeOffset> AcknowledgeSafetyNoticeAsync(
        Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO safety_acknowledgements (account_id, acknowledged_at) VALUES ({accountId}, {now})
            ON CONFLICT (account_id) DO NOTHING
            """,
            cancellationToken);
        return await FindSafetyAcknowledgementAsync(accountId, cancellationToken)
            ?? throw new InvalidOperationException("The acknowledgement was not stored.");
    }

    // Writes the timeouts due at now (S8) among invites, ended when their time ran out.
    private static Task<int> ExpireTimedOutAsync(IQueryable<InviteRow> invites, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = now - Invite.Lifetime;
        return invites
            .Where(row => row.State == InviteNames.Pending && row.SentAt <= due)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.State, InviteNames.Expired)
                    .SetProperty(row => row.ExpiryCause, InviteNames.Of(ExpiryCause.Timeout))
                    .SetProperty(row => row.EndedAt, row => row.SentAt + Invite.Lifetime),
                cancellationToken);
    }

    private sealed class PairTransaction(
        ClimbOnDbContext db,
        IDbContextTransaction transaction,
        Guid actorId,
        PairAccount? actor,
        PairAccount? other,
        IReadOnlyList<Invite> invites,
        bool connected) : IPairTransaction
    {
        public PairAccount? Actor => actor;

        public PairAccount? Other => other;

        public IReadOnlyList<Invite> Invites => invites;

        public bool Connected => connected;

        // Under the actor's FOR NO KEY UPDATE lock, which every invite from the actor takes first (S31).
        public Task<int> CountPendingSentAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            var due = now - Invite.Lifetime;
            return db.Invites.CountAsync(
                row => row.SenderId == actorId && row.State == InviteNames.Pending && row.SentAt > due, cancellationToken);
        }

        public async Task AddAsync(Invite invite, CancellationToken cancellationToken)
        {
            db.Invites.Add(new InviteRow
            {
                Id = invite.Id,
                SenderId = invite.SenderId,
                RecipientId = invite.RecipientId,
                SentAt = invite.SentAt,
                State = InviteNames.Of(invite.StoredState),
            });
            await SaveAsync(cancellationToken);
        }

        public Task DeclineAsync(Invite declined, CancellationToken cancellationToken) => EndAsync(declined, cancellationToken);

        public async Task AcceptAsync(Invite accepted, Guid connectionId, CancellationToken cancellationToken)
        {
            await EndAsync(accepted, cancellationToken);
            var (low, high) = PairLocks.Order(accepted.SenderId, accepted.RecipientId);
            db.Connections.Add(new ConnectionRow
            {
                Id = connectionId,
                PairLow = low,
                PairHigh = high,
                State = InviteNames.ConnectionActive,
                CreatedAt = accepted.EndedAt!.Value,
                InviteId = accepted.Id,
            });
            await SaveAsync(cancellationToken);
        }

        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            db.ChangeTracker.Clear();
            return transaction.DisposeAsync();
        }

        // Only a pending invite is answered (Invite.Accept, Invite.Decline), so the stored row is still pending.
        private async Task EndAsync(Invite ended, CancellationToken cancellationToken)
        {
            var updated = await db.Invites
                .Where(row => row.Id == ended.Id && row.State == InviteNames.Pending)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.State, InviteNames.Of(ended.StoredState))
                        .SetProperty(row => row.EndedAt, ended.EndedAt),
                    cancellationToken);
            if (updated != 1)
            {
                throw new InvalidOperationException("The invite was not pending under the pair lock.");
            }
        }

        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}

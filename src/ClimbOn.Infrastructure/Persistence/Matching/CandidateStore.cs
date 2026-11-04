using System.Data;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Invites;
using ClimbOn.Domain.Matching;
using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Persistence.Invites;
using ClimbOn.Infrastructure.Persistence.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Infrastructure.Persistence.Matching;

// Design §2 'Matching' step 1. Reads no grade (S14). Suspension and deactivation arrive in M2, blocks (S24.1) in M2.
public sealed partial class CandidateStore(ClimbOnDbContext db, ILogger<CandidateStore> logger) : ICandidateStore
{
    public async Task<IReadOnlyList<Climber>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, PrefilterScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var accounts = db.Accounts.AsNoTracking()
            .Where(account => account.Id != requesterId
                && account.EmailConfirmedAt != null
                && account.LastActivityAt >= activeSince);
        if (scope.Only is { } only)
        {
            accounts = accounts.Where(account => account.Id == only);
        }

        if (scope.PairTermsAt is { } at)
        {
            accounts = WithoutPairs(accounts, requesterId, at);
        }

        return await ReadAsync(accounts, box, cancellationToken);
    }

    public async Task<IReadOnlyList<Climber>> FindAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        accountIds.Count == 0
            ? []
            : await ReadAsync(db.Accounts.AsNoTracking().Where(account => accountIds.Contains(account.Id)), null, cancellationToken);

    // S117: no invite between the two pending at at, in either direction, and no active or paused connection.
    private IQueryable<AccountRow> WithoutPairs(IQueryable<AccountRow> accounts, Guid requesterId, DateTimeOffset at)
    {
        var sentAfter = at - Invite.Lifetime;
        return accounts.Where(account =>
            !db.Invites.Any(invite => invite.State == InviteNames.Pending && invite.SentAt > sentAfter
                && ((invite.SenderId == requesterId && invite.RecipientId == account.Id)
                    || (invite.SenderId == account.Id && invite.RecipientId == requesterId)))
            && !db.Connections.Any(connection =>
                (connection.State == InviteNames.ConnectionActive || connection.State == InviteNames.ConnectionPaused)
                && ((connection.PairLow == requesterId && connection.PairHigh == account.Id)
                    || (connection.PairLow == account.Id && connection.PairHigh == requesterId))));
    }

    // One snapshot for the points and the profiles, so that a climber's point is its profile's postcode's: a
    // repeatable read of its own, or the caller's transaction, which holds the profile rows FOR SHARE.
    private async Task<IReadOnlyList<Climber>> ReadAsync(
        IQueryable<AccountRow> accounts, BoundingBox? box, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var points = from account in accounts
                     join profile in db.Profiles.AsNoTracking() on account.Id equals profile.AccountId
                     join postcode in db.Postcodes.AsNoTracking() on profile.Postcode equals postcode.Code
                     select new { account.Id, account.LastActivityAt, postcode.Latitude, postcode.Longitude };
        if (box is { } within)
        {
            points = points.Where(row => row.Latitude >= within.South && row.Latitude <= within.North
                && row.Longitude >= within.West && row.Longitude <= within.East);
        }

        var rows = await points.ToListAsync(cancellationToken);

        // A stored profile that today's rules refuse is no candidate, rather than an error for every requester
        // near it.
        var profiles = await ProfileStore.ReadAsync(
            db, rows.Select(row => row.Id).ToList(), cancellationToken, (id, error) => LogProfileSkipped(id, error.Code));
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return rows.Where(row => profiles.ContainsKey(row.Id))
            .Select(row => new Climber(
                row.Id, profiles[row.Id], new GeoPoint(row.Latitude, row.Longitude), row.LastActivityAt))
            .ToList();
    }

    [LoggerMessage(
        EventName = "CandidateProfileSkipped",
        Level = LogLevel.Warning,
        Message = "Skipped the stored profile of account {AccountId}, which today's rules refuse: {Code}")]
    private partial void LogProfileSkipped(Guid accountId, string code);
}

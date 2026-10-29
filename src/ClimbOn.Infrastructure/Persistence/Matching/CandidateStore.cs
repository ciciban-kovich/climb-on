using System.Data;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Matching;
using ClimbOn.Infrastructure.Persistence.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Infrastructure.Persistence.Matching;

// Design §2 'Matching' step 1. Reads no grade (S14). Suspension and deactivation arrive in M2, the S117 terms
// with invites and connections (task 19), blocks (S24.1) in M2.
public sealed partial class CandidateStore(ClimbOnDbContext db, ILogger<CandidateStore> logger) : ICandidateStore
{
    public async Task<IReadOnlyList<Climber>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, CancellationToken cancellationToken)
    {
        // One snapshot for the points and the profiles, so that a candidate's point is its profile's postcode's.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await (
                from account in db.Accounts.AsNoTracking()
                join profile in db.Profiles.AsNoTracking() on account.Id equals profile.AccountId
                join postcode in db.Postcodes.AsNoTracking() on profile.Postcode equals postcode.Code
                where account.Id != requesterId
                    && account.EmailConfirmedAt != null
                    && account.LastActivityAt >= activeSince
                    && postcode.Latitude >= box.South && postcode.Latitude <= box.North
                    && postcode.Longitude >= box.West && postcode.Longitude <= box.East
                select new { account.Id, account.LastActivityAt, postcode.Latitude, postcode.Longitude })
            .ToListAsync(cancellationToken);
        // A stored profile that today's rules refuse is no candidate, rather than an error for every requester
        // near it.
        var profiles = await ProfileStore.ReadAsync(
            db, rows.Select(row => row.Id).ToList(), cancellationToken, (id, error) => LogProfileSkipped(id, error.Code));
        await transaction.CommitAsync(cancellationToken);

        return rows.Where(row => profiles.ContainsKey(row.Id))
            .Select(row => new Climber(
                row.Id, profiles[row.Id], new GeoPoint(row.Latitude, row.Longitude), row.LastActivityAt))
            .ToList();
    }

    [LoggerMessage(
        EventName = "CandidateProfileSkipped",
        Level = LogLevel.Warning,
        Message = "Skipped the stored profile of account {AccountId} as a candidate: {Code}")]
    private partial void LogProfileSkipped(Guid accountId, string code);
}

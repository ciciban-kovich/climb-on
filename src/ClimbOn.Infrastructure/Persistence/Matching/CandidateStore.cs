using ClimbOn.Application.Matching;
using ClimbOn.Domain.Geo;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Matching;

// Design §2 'Matching' step 1. Reads no grade (S14). Suspension and deactivation arrive in M2, the S117 terms
// with invites and connections (task 19), blocks (S24.1) in M2.
public sealed class CandidateStore(ClimbOnDbContext db) : ICandidateStore
{
    public async Task<IReadOnlyList<PrefilteredCandidate>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, CancellationToken cancellationToken)
    {
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
        return rows.Select(row => new PrefilteredCandidate(row.Id, new GeoPoint(row.Latitude, row.Longitude), row.LastActivityAt))
            .ToList();
    }
}

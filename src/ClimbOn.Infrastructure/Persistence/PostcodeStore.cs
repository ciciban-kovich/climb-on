using ClimbOn.Application.Postcodes;
using ClimbOn.Domain.Geo;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class PostcodeStore(ClimbOnDbContext db) : IPostcodeStore
{
    public async Task<StoredPostcode?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var row = await db.Postcodes.AsNoTracking().SingleOrDefaultAsync(postcode => postcode.Code == code, cancellationToken);
        return row is null ? null : new StoredPostcode(row.Code, new GeoPoint(row.Latitude, row.Longitude), row.Retired);
    }
}

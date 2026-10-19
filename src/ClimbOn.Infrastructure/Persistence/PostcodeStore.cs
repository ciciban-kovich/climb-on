using ClimbOn.Application.Postcodes;
using ClimbOn.Domain.Geo;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence;

public sealed class PostcodeStore(ClimbOnDbContext db) : IPostcodeStore
{
    public async Task<StoredPostcode?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var row = await db.Postcodes.AsNoTracking().SingleOrDefaultAsync(postcode => postcode.Code == code, cancellationToken);
        return row is null ? null : ToStored(row);
    }

    public async Task<PostcodeImportResult> ApplyAsync(
        Func<IReadOnlyCollection<StoredPostcode>, PostcodeImportPlan> plan, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // EXCLUSIVE blocks every other writer until commit and still lets readers in.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE postcodes IN EXCLUSIVE MODE", cancellationToken);
        var rows = await db.Postcodes.ToDictionaryAsync(row => row.Code, StringComparer.Ordinal, cancellationToken);

        var planned = plan(rows.Values.Select(ToStored).ToList());
        foreach (var write in planned.Writes)
        {
            if (!rows.TryGetValue(write.Code, out var row))
            {
                row = new Postcode { Code = write.Code };
                db.Postcodes.Add(row);
            }

            row.Latitude = write.Point.Latitude;
            row.Longitude = write.Point.Longitude;
            row.Retired = write.Retired;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planned.Result;
    }

    private static StoredPostcode ToStored(Postcode row) =>
        new(row.Code, new GeoPoint(row.Latitude, row.Longitude), row.Retired);
}

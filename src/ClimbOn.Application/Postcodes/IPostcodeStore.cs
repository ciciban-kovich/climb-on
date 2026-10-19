using ClimbOn.Domain.Geo;

namespace ClimbOn.Application.Postcodes;

// A row of the postcode reference table: its reference point (S99) and whether it is retired (S102).
public sealed record StoredPostcode(string Code, GeoPoint Point, bool Retired);

public interface IPostcodeStore
{
    Task<StoredPostcode?> FindAsync(string code, CancellationToken cancellationToken);

    // Reads every row, then writes the plan's rows, in one transaction no other writer of the table
    // can interleave with. Nothing is written if any of it fails (S101.2).
    Task<PostcodeImportResult> ApplyAsync(
        Func<IReadOnlyCollection<StoredPostcode>, PostcodeImportPlan> plan, CancellationToken cancellationToken);
}

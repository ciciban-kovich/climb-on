using ClimbOn.Domain.Geo;

namespace ClimbOn.Application.Postcodes;

// A row of the postcode reference table: its reference point (S99) and whether it is retired (S102).
public sealed record StoredPostcode(string Code, GeoPoint Point, bool Retired);

public interface IPostcodeStore
{
    Task<StoredPostcode?> FindAsync(string code, CancellationToken cancellationToken);
}

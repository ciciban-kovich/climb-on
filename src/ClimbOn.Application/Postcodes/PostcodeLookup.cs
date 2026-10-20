using ClimbOn.Domain.Geo;

namespace ClimbOn.Application.Postcodes;

// S16, S102.3: the reference point of any stored postcode, retired or not.
public sealed class PostcodeLookup(IPostcodeStore store)
{
    // Null when the postcode is not in the reference table.
    public async Task<GeoPoint?> FindPointAsync(string postcode, CancellationToken cancellationToken) =>
        (await store.FindAsync(postcode, cancellationToken))?.Point;
}

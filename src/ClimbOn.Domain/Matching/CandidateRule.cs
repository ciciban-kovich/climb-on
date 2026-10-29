using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Domain.Matching;

// A climber as matching sees them: the profile, its postcode's reference point (S99) and the last activity.
public sealed record Climber(Guid AccountId, Profile Profile, GeoPoint ReferencePoint, DateTimeOffset LastActivity);

// S4: whether B is a candidate for A. Every term is symmetric in A and B (S10), and none reads a grade (S14).
public static class CandidateRule
{
    public static bool IsCandidate(Climber a, Climber b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return IsWithinTravel(a, b) && AcceptEachOther(a.Profile, b.Profile) && SharedVariants(a.Profile, b.Profile).Any();
    }

    // S4.1 with S16 and S52.
    public static bool IsWithinTravel(Climber a, Climber b) =>
        IsWithinTravel(DistanceKm(a, b), a.Profile.MaxTravelKm, b.Profile.MaxTravelKm);

    public static bool IsWithinTravel(double km, int maxA, int maxB) =>
        Distance.WholeKilometres(km) <= Math.Min(maxA, maxB);

    // S4.2
    public static bool AcceptEachOther(Profile a, Profile b) =>
        a.AcceptedSexes.Contains(b.Sex) && b.AcceptedSexes.Contains(a.Sex);

    // S4.3: the variants both have and for which both meet S12, in variant order.
    public static IEnumerable<DisciplineVariant> SharedVariants(Profile a, Profile b) =>
        a.Variants.Intersect(b.Variants)
            .Where(variant => variant.IsMetBy(a.Certification) && variant.IsMetBy(b.Certification))
            .Order();

    // S16
    public static double DistanceKm(Climber a, Climber b) => Distance.Kilometres(a.ReferencePoint, b.ReferencePoint);
}

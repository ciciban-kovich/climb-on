using ClimbOn.Domain.Profiles;

namespace ClimbOn.Domain.Matching;

// S60.2, on the internal scale (S13.3).
public static class GradeFit
{
    // The best fit over the variants of S4.3; 0 when there are none.
    public static int Of(Profile a, Profile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return CandidateRule.SharedVariants(a, b)
            .Select(variant => Of(EntryFor(a, variant), EntryFor(b, variant)))
            .DefaultIfEmpty(0)
            .Max();
    }

    // The number of directions that hold: A's own range meets B's partner range, and the reverse.
    private static int Of(DisciplineEntry a, DisciplineEntry b) =>
        (Intersect(a.Own, b.Partner) ? 1 : 0) + (Intersect(b.Own, a.Partner) ? 1 : 0);

    private static bool Intersect(GradeRange x, GradeRange y) =>
        x.Lowest.Position <= y.Highest.Position && y.Lowest.Position <= x.Highest.Position;

    private static DisciplineEntry EntryFor(Profile profile, DisciplineVariant variant) =>
        profile.Disciplines.Single(entry => entry.Variants.Contains(variant));
}

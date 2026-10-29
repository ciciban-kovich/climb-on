using ClimbOn.Domain.Profiles;

namespace ClimbOn.Domain.Matching;

// The window overlap of the specs glossary: the non-empty intersections of two climbers' windows on the same weekday.
public static class WindowOverlap
{
    public const int MinutesPerHour = 60;

    // By day, then start. A climber's own windows never overlap (S61), so no two of these overlap either.
    public static IReadOnlyList<AvailabilityWindow> Of(IEnumerable<AvailabilityWindow> a, IEnumerable<AvailabilityWindow> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var others = b.ToList();
        return a.SelectMany(window => others
                .Where(window.Overlaps)
                .Select(other => new AvailabilityWindow(
                    window.Day, Math.Max(window.StartHour, other.StartHour), Math.Min(window.EndHour, other.EndHour))))
            .OrderBy(window => window.Day)
            .ThenBy(window => window.StartHour)
            .ToList();
    }

    // S60.1
    public static int TotalMinutes(IEnumerable<AvailabilityWindow> a, IEnumerable<AvailabilityWindow> b) =>
        Of(a, b).Sum(window => (window.EndHour - window.StartHour) * MinutesPerHour);
}

using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Profiles;

// A weekday plus a start and end time (specs glossary), in whole hours; an end of 24 is the end of the day.
public sealed record AvailabilityWindow(DayOfWeek Day, int StartHour, int EndHour)
{
    public const string InvalidCode = "window_invalid";
    public const string OverlapCode = "windows_overlap";
    public const string RequiredCode = "windows_required";

    public const int HoursPerDay = 24;

    public static IReadOnlyList<string> Codes { get; } = [InvalidCode, OverlapCode, RequiredCode];

    public bool Overlaps(AvailabilityWindow other) =>
        Day == other.Day && StartHour < other.EndHour && other.StartHour < EndHour;

    // S3: one or more windows. S61: each ends later on the same day than it starts, and windows on one
    // weekday do not overlap; windows that only touch (18-20, 20-22) do not. Returns them by day and start.
    public static IReadOnlyList<AvailabilityWindow> EnsureValid(IReadOnlyCollection<AvailabilityWindow>? windows)
    {
        if (windows is null || windows.Count == 0)
        {
            throw new DomainError(RequiredCode);
        }

        if (windows.Any(window => window is null
            || !Enum.IsDefined(window.Day)
            || window.StartHour < 0
            || window.EndHour > HoursPerDay
            || window.EndHour <= window.StartHour))
        {
            throw new DomainError(InvalidCode);
        }

        var ordered = windows.OrderBy(window => window.Day).ThenBy(window => window.StartHour).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index - 1].Overlaps(ordered[index]))
            {
                throw new DomainError(OverlapCode);
            }
        }

        return ordered;
    }
}

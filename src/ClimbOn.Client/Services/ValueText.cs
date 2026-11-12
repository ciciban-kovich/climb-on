namespace ClimbOn.Client.Services;

// The text keys for the api's wire names (ProfileRequest's values), so the client shows a value it was sent in
// the climber's language. Which values exist and which are valid is the api's (C7).
public static class ValueText
{
    public static IReadOnlyList<string> Sexes { get; } = ["female", "male", "other"];

    public static IReadOnlyList<string> RopeScales { get; } = ["norwegian", "french"];

    public static IReadOnlyList<string> Disciplines { get; } = ["bouldering", "top-rope", "lead", "multipitch", "ice"];

    public static IReadOnlyList<string> Venues { get; } = ["indoor", "outdoor"];

    public static IReadOnlyList<string> Certifications { get; } = ["none", "brattkort-topptau", "brattkort-led"];

    public static IReadOnlyList<string> Days { get; } =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public static string Sex(string? value) => "sex_" + value;

    public static string RopeScale(string? value) => "rope_scale_" + value;

    public static string Discipline(string? value) => "discipline_" + value;

    public static string Venue(string? value) => "venue_" + value;

    public static string Certification(string? value) => "certification_" + value;

    public static string Day(string? value) => "day_" + value;

    // A window hour (S61) as a time of day: 24 is the end of the day.
    public static string Hour(int? hour) => hour is { } value ? $"{value:00}:00" : string.Empty;
}

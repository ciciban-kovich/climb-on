using System.Globalization;
using System.Text;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Grades;
using ClimbOn.Domain.Postcodes;

namespace ClimbOn.Domain.Profiles;

public enum Sex
{
    Female,
    Male,
    Other,
}

// What a climber sends to save a profile (S3, without the photo). Every field may be missing.
// Names are the wire names of WireNames; grades are as the climber enters them.
public sealed record ProfileDraft(
    string? DisplayName,
    string? Sex,
    IReadOnlyList<string?>? AcceptedSexes,
    string? RopeScale,
    IReadOnlyList<DisciplineEntryDraft?>? Disciplines,
    string? Certification,
    bool? OwnGear,
    string? Postcode,
    int? MaxTravelKm,
    IReadOnlyList<Guid>? HomeGyms,
    IReadOnlyList<WindowDraft?>? Windows);

// One entry for venues listed together shares its ranges across them (S3).
public sealed record DisciplineEntryDraft(
    string? Discipline, IReadOnlyList<string?>? Venues, GradeRangeDraft? Own, GradeRangeDraft? Partner);

public sealed record GradeRangeDraft(string? Lowest, string? Highest);

public sealed record WindowDraft(string? Day, int? StartHour, int? EndHour);

// Lowest is never harder than Highest; both are on one scale.
public sealed record GradeRange(Grade Lowest, Grade Highest);

// The variants of one discipline with the same ranges: two venues when the ranges are shared (S3).
public sealed record DisciplineEntry(Discipline Discipline, IReadOnlyList<Venue> Venues, GradeRange Own, GradeRange Partner)
{
    public IEnumerable<DisciplineVariant> Variants =>
        Venues.Select(venue => DisciplineVariants.Of(Discipline, venue)!.Value);
}

// S3 without the photo (S55, M2).
public sealed class Profile
{
    public const string DisplayNameInvalidCode = "display_name_invalid";
    public const string SexInvalidCode = "sex_invalid";
    public const string AcceptedSexesInvalidCode = "accepted_sexes_invalid";
    public const string RopeScaleInvalidCode = "rope_scale_invalid";
    public const string VariantInvalidCode = "discipline_variant_invalid";
    public const string VariantDuplicateCode = "discipline_variant_duplicate";
    public const string GradeInvalidCode = "grade_invalid";
    public const string GradeRangeInvalidCode = "grade_range_invalid";
    public const string CertificationInvalidCode = "certification_invalid";
    public const string OwnGearRequiredCode = "own_gear_required";
    public const string TravelDistanceInvalidCode = "travel_distance_invalid";

    public const int MaximumDisplayNameLength = 50;
    public const int MinimumTravelKm = 1;
    public const int MaximumTravelKm = 100;

    private Profile(
        string displayName,
        Sex sex,
        IReadOnlySet<Sex> acceptedSexes,
        GradeScale ropeScale,
        IReadOnlyList<DisciplineEntry> disciplines,
        BelayCertification certification,
        bool ownGear,
        string postcode,
        int maxTravelKm,
        IReadOnlyList<Guid> homeGyms,
        IReadOnlyList<AvailabilityWindow> windows)
    {
        DisplayName = displayName;
        Sex = sex;
        AcceptedSexes = acceptedSexes;
        RopeScale = ropeScale;
        Disciplines = disciplines;
        Certification = certification;
        OwnGear = ownGear;
        Postcode = postcode;
        MaxTravelKm = maxTravelKm;
        HomeGyms = homeGyms;
        Windows = windows;
    }

    public static IReadOnlyList<string> Codes { get; } =
    [
        DisplayNameInvalidCode, SexInvalidCode, AcceptedSexesInvalidCode, RopeScaleInvalidCode, VariantInvalidCode,
        VariantDuplicateCode, GradeInvalidCode, GradeRangeInvalidCode, CertificationInvalidCode, OwnGearRequiredCode,
        TravelDistanceInvalidCode, .. AvailabilityWindow.Codes,
    ];

    // Trimmed; never translated (S106).
    public string DisplayName { get; }

    public Sex Sex { get; }

    // Every sex when the climber chose none (S3: default any).
    public IReadOnlySet<Sex> AcceptedSexes { get; }

    // S13.1: the climber's rope scale; every rope grade in the profile is on it.
    public GradeScale RopeScale { get; }

    // Each variant at most once.
    public IReadOnlyList<DisciplineEntry> Disciplines { get; }

    public BelayCertification Certification { get; }

    public bool OwnGear { get; }

    // Well-formed only: whether the table accepts it is decided when it is created or changed (S15).
    public string Postcode { get; }

    public int MaxTravelKm { get; }

    // Each gym once, in the order first given.
    public IReadOnlyList<Guid> HomeGyms { get; }

    // By day, then start.
    public IReadOnlyList<AvailabilityWindow> Windows { get; }

    public IEnumerable<DisciplineVariant> Variants => Disciplines.SelectMany(entry => entry.Variants);

    // Every S3 rule that needs nothing stored. Throws DomainError for the first rule a field breaks.
    public static Profile Create(ProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var displayName = EnsureDisplayName(draft.DisplayName);
        var sex = WireNames.Parse<Sex>(draft.Sex) ?? throw new DomainError(SexInvalidCode);
        var acceptedSexes = EnsureAcceptedSexes(draft.AcceptedSexes);
        var ropeScale = WireNames.Parse<GradeScale>(draft.RopeScale) is { } scale && scale.Family() == GradeFamily.Rope
            ? scale
            : throw new DomainError(RopeScaleInvalidCode);
        var disciplines = EnsureDisciplines(draft.Disciplines, ropeScale);
        var certification = WireNames.Parse<BelayCertification>(draft.Certification)
            ?? throw new DomainError(CertificationInvalidCode);
        var ownGear = draft.OwnGear ?? throw new DomainError(OwnGearRequiredCode);
        PostcodeRules.EnsureWellFormed(draft.Postcode);
        var maxTravelKm = draft.MaxTravelKm is { } km and >= MinimumTravelKm and <= MaximumTravelKm
            ? km
            : throw new DomainError(TravelDistanceInvalidCode);
        var windows = AvailabilityWindow.EnsureValid(draft.Windows?.Select(ToWindow).ToList());

        return new Profile(
            displayName,
            sex,
            acceptedSexes,
            ropeScale,
            disciplines,
            certification,
            ownGear,
            draft.Postcode,
            maxTravelKm,
            (draft.HomeGyms ?? []).Distinct().ToList(),
            windows);
    }

    // The grade scale of a discipline's grades in a profile on this rope scale (S13.1, S13.2).
    public static GradeScale ScaleOf(Discipline discipline, GradeScale ropeScale) => discipline.GradeFamily() switch
    {
        GradeFamily.Rope => ropeScale,
        var family => GradeScales.For(family).Single(),
    };

    // 1 to 50 characters (text elements) after trimming, none of them a control character.
    private static string EnsureDisplayName(string? value)
    {
        var name = value?.Trim();
        if (name is null
            || new StringInfo(name).LengthInTextElements is < 1 or > MaximumDisplayNameLength
            || name.Any(char.IsControl))
        {
            throw new DomainError(DisplayNameInvalidCode);
        }

        return name;
    }

    private static HashSet<Sex> EnsureAcceptedSexes(IReadOnlyList<string?>? names)
    {
        if (names is null)
        {
            return [.. Enum.GetValues<Sex>()];
        }

        var sexes = names.Select(WireNames.Parse<Sex>).ToList();
        return sexes.Count > 0 && sexes.All(sex => sex is not null)
            ? sexes.Select(sex => sex!.Value).ToHashSet()
            : throw new DomainError(AcceptedSexesInvalidCode);
    }

    private static List<DisciplineEntry> EnsureDisciplines(IReadOnlyList<DisciplineEntryDraft?>? drafts, GradeScale ropeScale)
    {
        var entries = new List<DisciplineEntry>();
        var seen = new HashSet<DisciplineVariant>();
        foreach (var draft in drafts ?? [])
        {
            if (draft is null
                || WireNames.Parse<Discipline>(draft.Discipline) is not { } discipline
                || draft.Venues is not { Count: > 0 } venueNames)
            {
                throw new DomainError(VariantInvalidCode);
            }

            var venues = new List<Venue>();
            foreach (var name in venueNames)
            {
                if (WireNames.Parse<Venue>(name) is not { } venue
                    || venues.Contains(venue)
                    || DisciplineVariants.Of(discipline, venue) is not { } variant)
                {
                    throw new DomainError(VariantInvalidCode);
                }

                if (!seen.Add(variant))
                {
                    throw new DomainError(VariantDuplicateCode);
                }

                venues.Add(venue);
            }

            var scale = ScaleOf(discipline, ropeScale);
            entries.Add(new DisciplineEntry(
                discipline, venues.Order().ToList(), EnsureRange(draft.Own, scale), EnsureRange(draft.Partner, scale)));
        }

        return entries;
    }

    private static GradeRange EnsureRange(GradeRangeDraft? draft, GradeScale scale)
    {
        if (!Grade.TryCreate(scale, draft?.Lowest, out var lowest) || !Grade.TryCreate(scale, draft?.Highest, out var highest))
        {
            throw new DomainError(GradeInvalidCode);
        }

        return lowest.Position <= highest.Position
            ? new GradeRange(lowest, highest)
            : throw new DomainError(GradeRangeInvalidCode);
    }

    private static AvailabilityWindow ToWindow(WindowDraft? draft) =>
        draft is { StartHour: { } start, EndHour: { } end } && WireNames.Parse<DayOfWeek>(draft.Day) is { } day
            ? new AvailabilityWindow(day, start, end)
            : throw new DomainError(AvailabilityWindow.InvalidCode);
}

// The names the climber API uses for the profile's values: an enum member in lower case, its words joined
// by hyphens (TopRope is top-rope, BrattkortLed is brattkort-led, Tuesday is tuesday).
public static class WireNames
{
    public static string Of<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }

        var builder = new StringBuilder();
        foreach (var character in value.ToString())
        {
            if (char.IsUpper(character) && builder.Length > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    // Null when name is no member's wire name; matching is exact.
    public static T? Parse<T>(string? name)
        where T : struct, Enum =>
        Enum.GetValues<T>().Where(value => Of(value) == name).Select(value => (T?)value).SingleOrDefault();
}

using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Domain.Tests.Matching;

// Climbers for the matching examples: a valid draft to vary with `with`, and the points the distances are taken from.
internal static class Climbers
{
    public static readonly GeoPoint Oslo = new(59.9139, 10.7522);

    public static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly ProfileDraft Draft = new(
        "Kari",
        "female",
        null,
        "norwegian",
        [new DisciplineEntryDraft("bouldering", ["indoor"], Range("5", "6B"), Range("5", "6B"))],
        "none",
        true,
        "0150",
        25,
        null,
        [new WindowDraft("tuesday", 18, 21)]);

    public static GradeRangeDraft Range(string lowest, string highest) => new(lowest, highest);

    // A point the given distance due north of Oslo, along its meridian.
    public static GeoPoint NorthOfOslo(double km) =>
        new(Oslo.Latitude + (km / Distance.EarthRadiusKm * 180 / Math.PI), Oslo.Longitude);

    public static Climber Climber(
        ProfileDraft? draft = null, GeoPoint? at = null, DateTimeOffset? lastActivity = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Profile.Create(draft ?? Draft), at ?? Oslo, lastActivity ?? Noon);
}

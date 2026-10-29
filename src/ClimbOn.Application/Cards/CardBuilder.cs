using System.Globalization;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Cards;

// One discipline entry as another climber sees it: the shown climber's own range (S13.5).
public sealed record CardDiscipline(Discipline Discipline, IReadOnlyList<Venue> Venues, string Lowest, string Highest);

// S51 without the photo (S55, M2). Holds nothing S50 or S53 excludes. CertificationLabelKey is the text key of
// the S22 label.
public sealed record ClimberCard(
    Guid AccountId,
    string DisplayName,
    Sex Sex,
    IReadOnlyList<CardDiscipline> Disciplines,
    BelayCertification Certification,
    string CertificationLabelKey,
    bool OwnGear,
    IReadOnlyList<Gym> HomeGyms,
    IReadOnlyList<AvailabilityWindow> Overlap,
    string Distance);

// The only way any endpoint builds another climber's data (S54).
public static class CardBuilder
{
    // S22
    public const string SelfDeclaredKey = "card_certification_self_declared";

    // S52: a card distance below this many whole km is shown as UnderFloor.
    public const int FloorKm = 2;

    public const string UnderFloor = "< 2 km";

    // gyms holds at least the shown climber's home gyms.
    public static ClimberCard For(Climber viewer, Climber shown, IReadOnlyDictionary<Guid, Gym> gyms)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(gyms);

        var profile = shown.Profile;
        return new ClimberCard(
            shown.AccountId,
            profile.DisplayName,
            profile.Sex,
            profile.Disciplines.Select(entry => new CardDiscipline(
                entry.Discipline,
                entry.Venues,
                entry.Own.Lowest.ShownTo(viewer.Profile.RopeScale),
                entry.Own.Highest.ShownTo(viewer.Profile.RopeScale))).ToList(),
            profile.Certification,
            SelfDeclaredKey,
            profile.OwnGear,
            profile.HomeGyms.Select(id => gyms[id]).ToList(),
            WindowOverlap.Of(viewer.Profile.Windows, profile.Windows),
            DistanceShown(CandidateRule.DistanceKm(viewer, shown)));
    }

    // S52
    public static string DistanceShown(double km) =>
        Distance.WholeKilometres(km) is var whole && whole < FloorKm ? UnderFloor : string.Create(CultureInfo.InvariantCulture, $"{whole:0} km");
}

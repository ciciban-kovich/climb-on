using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// Values are lower-case names with hyphens: sexes female, male, other; rope scales norwegian, french;
// disciplines bouldering, top-rope, lead, multipitch, ice; venues indoor, outdoor; certifications none,
// brattkort-topptau, brattkort-led; days monday to sunday. Grades are on the rope scale for top-rope, lead
// and multipitch, on Font for bouldering and on WI for ice. Window hours are whole, 0 to 24.
public sealed record ProfileRequest(
    string? DisplayName,
    string? Sex,
    IReadOnlyList<string?>? AcceptedSexes,
    string? RopeScale,
    IReadOnlyList<DisciplineEntryRequest?>? Disciplines,
    string? Certification,
    bool? OwnGear,
    string? Postcode,
    int? MaxTravelKm,
    IReadOnlyList<Guid>? HomeGyms,
    IReadOnlyList<WindowRequest?>? Windows);

// Venues listed in one entry share its grade ranges.
public sealed record DisciplineEntryRequest(
    string? Discipline, IReadOnlyList<string?>? Venues, GradeRangeRequest? Own, GradeRangeRequest? Partner);

public sealed record GradeRangeRequest(string? Lowest, string? Highest);

public sealed record WindowRequest(string? Day, int? Start, int? End);

// The profile as its owner entered it (S13.4).
public sealed record ProfileResponse(
    string DisplayName,
    string Sex,
    IReadOnlyList<string> AcceptedSexes,
    string RopeScale,
    IReadOnlyList<DisciplineEntryResponse> Disciplines,
    string Certification,
    bool OwnGear,
    string Postcode,
    int MaxTravelKm,
    IReadOnlyList<Guid> HomeGyms,
    IReadOnlyList<WindowResponse> Windows);

public sealed record DisciplineEntryResponse(
    string Discipline, IReadOnlyList<string> Venues, GradeRangeResponse Own, GradeRangeResponse Partner);

public sealed record GradeRangeResponse(string Lowest, string Highest);

public sealed record WindowResponse(string Day, int Start, int End);

public static class ProfileEndpoints
{
    private const string Tag = "Profile";
    private const string Route = "/api/climber/profile";

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, GetAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPut(Route, SaveAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    private static async Task<Results<Ok<ProfileResponse>, NotFound>> GetAsync(
        HttpContext context, IProfileStore profiles, CancellationToken cancellationToken) =>
        await profiles.FindAsync(ClimberSessions.Current(context).AccountId, cancellationToken) is { } profile
            ? TypedResults.Ok(ToResponse(profile))
            : TypedResults.NotFound();

    private static async Task<Ok<ProfileResponse>> SaveAsync(
        ProfileRequest request, HttpContext context, SaveProfile save, CancellationToken cancellationToken)
    {
        var profile = await save.RunAsync(ClimberSessions.Current(context).AccountId, ToDraft(request), cancellationToken);
        return TypedResults.Ok(ToResponse(profile));
    }

    private static ProfileDraft ToDraft(ProfileRequest request) => new(
        request.DisplayName,
        request.Sex,
        request.AcceptedSexes,
        request.RopeScale,
        request.Disciplines?.Select(entry => entry is null
            ? null
            : new DisciplineEntryDraft(entry.Discipline, entry.Venues, ToDraft(entry.Own), ToDraft(entry.Partner))).ToList(),
        request.Certification,
        request.OwnGear,
        request.Postcode,
        request.MaxTravelKm,
        request.HomeGyms,
        request.Windows?.Select(window => window is null ? null : new WindowDraft(window.Day, window.Start, window.End)).ToList());

    private static GradeRangeDraft? ToDraft(GradeRangeRequest? range) =>
        range is null ? null : new GradeRangeDraft(range.Lowest, range.Highest);

    private static ProfileResponse ToResponse(Profile profile) => new(
        profile.DisplayName,
        WireNames.Of(profile.Sex),
        profile.AcceptedSexes.Order().Select(WireNames.Of).ToList(),
        WireNames.Of(profile.RopeScale),
        profile.Disciplines.Select(entry => new DisciplineEntryResponse(
            WireNames.Of(entry.Discipline),
            entry.Venues.Select(WireNames.Of).ToList(),
            ToResponse(entry.Own),
            ToResponse(entry.Partner))).ToList(),
        WireNames.Of(profile.Certification),
        profile.OwnGear,
        profile.Postcode,
        profile.MaxTravelKm,
        profile.HomeGyms,
        profile.Windows.Select(window => new WindowResponse(WireNames.Of(window.Day), window.StartHour, window.EndHour)).ToList());

    // S13.4
    private static GradeRangeResponse ToResponse(GradeRange range) => new(range.Lowest.OwnerView, range.Highest.OwnerView);
}

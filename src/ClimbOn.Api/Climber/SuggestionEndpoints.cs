using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// Another climber as the viewing climber sees them (S51, S54). Values are wire names as in ProfileRequest; grades
// are in the viewer's rope scale (S13.5); CertificationLabel is the S22 label in the viewer's language; Distance is
// the S52 text.
public sealed record ClimberCardResponse(
    Guid AccountId,
    string DisplayName,
    string Sex,
    IReadOnlyList<CardDisciplineResponse> Disciplines,
    string Certification,
    string CertificationLabel,
    bool OwnGear,
    IReadOnlyList<CardGymResponse> HomeGyms,
    IReadOnlyList<WindowResponse> Overlap,
    string Distance);

public sealed record CardDisciplineResponse(string Discipline, IReadOnlyList<string> Venues, GradeRangeResponse Grades);

// An inactive gym is one a profile listed before it was deactivated (S21).
public sealed record CardGymResponse(string Name, bool Active);

// In S60 order.
public sealed record SuggestionListResponse(IReadOnlyList<ClimberCardResponse> Candidates);

public static class SuggestionEndpoints
{
    private const string Tag = "Suggestions";

    public static IEndpointRouteBuilder MapSuggestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/climber/suggestions", GetAsync)
            .Produces<ErrorBody>(StatusCodes.Status403Forbidden)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    // The same card everywhere one climber sees another (S54).
    public static ClimberCardResponse ToResponse(ClimberCard card, Language language) => new(
        card.AccountId,
        card.DisplayName,
        WireNames.Of(card.Sex),
        card.Disciplines.Select(entry => new CardDisciplineResponse(
            WireNames.Of(entry.Discipline),
            entry.Venues.Select(WireNames.Of).ToList(),
            new GradeRangeResponse(entry.Lowest, entry.Highest))).ToList(),
        WireNames.Of(card.Certification),
        Texts.Get(card.CertificationLabelKey, language),
        card.OwnGear,
        card.HomeGyms.Select(gym => new CardGymResponse(gym.Name, gym.Active)).ToList(),
        card.Overlap.Select(window => new WindowResponse(WireNames.Of(window.Day), window.StartHour, window.EndHour)).ToList(),
        card.Distance);

    private static async Task<Ok<SuggestionListResponse>> GetAsync(
        HttpContext context, GetSuggestions suggestions, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        var cards = await suggestions.RunAsync(session.AccountId, cancellationToken);
        return TypedResults.Ok(new SuggestionListResponse(cards.Select(card => ToResponse(card, session.Language)).ToList()));
    }
}

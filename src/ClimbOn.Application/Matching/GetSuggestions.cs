using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Postcodes;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Application.Matching;

public interface ICandidateStore
{
    // Design §2 'Matching' step 1: every account other than the requester with a profile, a confirmed address
    // (S116), a last activity at or after activeSince (S107) and a reference point (S99, retained when retired,
    // S102.3) inside box. Each is read as one snapshot with its profile; one whose stored profile today's rules
    // refuse is left out and logged.
    Task<IReadOnlyList<Climber>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, CancellationToken cancellationToken);
}

// The requester's suggestion list (S4, S60), as climber cards.
public sealed partial class GetSuggestions(
    IAccountStore accounts,
    IProfileStore profiles,
    PostcodeLookup postcodes,
    ICandidateStore candidates,
    IClock clock,
    ILogger<GetSuggestions> logger)
{
    public const string EmailNotConfirmedCode = "email_not_confirmed";

    // S4.1 compares the distance rounded halves up (S52), so a candidate up to half a km past the travel
    // distance can still be in.
    public const double RoundingSlackKm = 0.5;

    public static IReadOnlyList<string> Codes { get; } = [EmailNotConfirmedCode];

    public async Task<IReadOnlyList<ClimberCard>> RunAsync(Guid requesterId, CancellationToken cancellationToken)
    {
        // S116, before any query of candidates.
        var account = await accounts.FindOwnAsync(requesterId, cancellationToken);
        if (account is not { EmailConfirmed: true })
        {
            throw new DomainError(EmailNotConfirmedCode, DomainErrorKind.Forbidden);
        }

        // Without a profile no S4 criterion can hold.
        var now = clock.UtcNow;
        if (await OwnProfileAsync(requesterId, cancellationToken) is not { } profile)
        {
            return [];
        }

        var requester = new Climber(requesterId, profile, await PointOfAsync(profile.Postcode, cancellationToken), now);
        var found = await candidates.PrefilterAsync(
            requesterId,
            BoundingBox.Around(requester.ReferencePoint, profile.MaxTravelKm + RoundingSlackKm),
            DormancyPolicy.ActiveIfLastActiveAtOrAfter(now),
            cancellationToken);
        var climbers = found.Where(candidate => CandidateRule.IsCandidate(requester, candidate)).ToList();

        var gyms = await profiles.FindGymsAsync(
            climbers.Append(requester).SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var activeGyms = gyms.Values.Where(gym => gym.Active).Select(gym => gym.Id).ToHashSet();
        return climbers
            .OrderBy(candidate => CandidateKey.For(requester, candidate, activeGyms), CandidateComparer.Instance)
            .Select(candidate => CardBuilder.For(requester, candidate, gyms))
            .ToList();
    }

    // A stored profile that today's rules refuse is still the requester's error to fix, with its code, but it is
    // logged as a refused candidate is.
    private async Task<Profile?> OwnProfileAsync(Guid requesterId, CancellationToken cancellationToken)
    {
        try
        {
            return await profiles.FindAsync(requesterId, cancellationToken);
        }
        catch (DomainError error)
        {
            LogOwnProfileRefused(requesterId, error.Code);
            throw;
        }
    }

    private async Task<GeoPoint> PointOfAsync(string postcode, CancellationToken cancellationToken) =>
        await postcodes.FindPointAsync(postcode, cancellationToken)
            ?? throw new InvalidOperationException("A profile's postcode is not in the reference table.");

    [LoggerMessage(
        EventName = "RequesterProfileRefused",
        Level = LogLevel.Warning,
        Message = "Refused the stored profile of account {AccountId} as the requester: {Code}")]
    private partial void LogOwnProfileRefused(Guid accountId, string code);
}

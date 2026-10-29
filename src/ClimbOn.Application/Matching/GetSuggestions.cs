using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Postcodes;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Matching;

namespace ClimbOn.Application.Matching;

// An account the prefilter let through: its reference point (S99, retained when retired, S102.3) and last activity.
public sealed record PrefilteredCandidate(Guid AccountId, GeoPoint ReferencePoint, DateTimeOffset LastActivity);

public interface ICandidateStore
{
    // Design §2 'Matching' step 1: every account other than the requester with a profile, a confirmed address
    // (S116), a last activity at or after activeSince (S107) and a reference point inside box.
    Task<IReadOnlyList<PrefilteredCandidate>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, CancellationToken cancellationToken);
}

// The requester's suggestion list (S4, S60), as climber cards.
public sealed class GetSuggestions(
    IAccountStore accounts, IProfileStore profiles, IPostcodeStore postcodes, ICandidateStore candidates, IClock clock)
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
        if (await profiles.FindAsync(requesterId, cancellationToken) is not { } profile)
        {
            return [];
        }

        var requester = new Climber(requesterId, profile, await PointOfAsync(profile.Postcode, cancellationToken), now);
        var found = await candidates.PrefilterAsync(
            requesterId,
            BoundingBox.Around(requester.ReferencePoint, profile.MaxTravelKm + RoundingSlackKm),
            DormancyPolicy.ActiveIfLastActiveAtOrAfter(now),
            cancellationToken);
        var foundProfiles = await profiles.FindManyAsync(found.Select(candidate => candidate.AccountId).ToList(), cancellationToken);
        var climbers = found
            .Where(candidate => foundProfiles.ContainsKey(candidate.AccountId))
            .Select(candidate => new Climber(
                candidate.AccountId, foundProfiles[candidate.AccountId], candidate.ReferencePoint, candidate.LastActivity))
            .Where(candidate => CandidateRule.IsCandidate(requester, candidate))
            .ToList();

        var gyms = await profiles.FindGymsAsync(
            climbers.Append(requester).SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var activeGyms = gyms.Values.Where(gym => gym.Active).Select(gym => gym.Id).ToHashSet();
        return climbers
            .OrderBy(candidate => CandidateKey.For(requester, candidate, activeGyms), CandidateComparer.Instance)
            .Select(candidate => CardBuilder.For(requester, candidate, gyms))
            .ToList();
    }

    private async Task<GeoPoint> PointOfAsync(string postcode, CancellationToken cancellationToken) =>
        (await postcodes.FindAsync(postcode, cancellationToken))?.Point
            ?? throw new InvalidOperationException("A profile's postcode is not in the reference table.");
}

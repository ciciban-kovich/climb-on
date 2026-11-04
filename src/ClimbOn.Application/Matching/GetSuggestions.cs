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

// Which accounts PrefilterAsync considers. Only, when set, restricts it to that one account. PairTermsAt, when set,
// applies S117 at that time; the candidate check of an invite leaves S117 out (design §2 'Invite validation order').
public sealed record PrefilterScope(Guid? Only, DateTimeOffset? PairTermsAt);

public interface ICandidateStore
{
    // Design §2 'Matching' step 1: every account other than the requester with a profile, a confirmed address
    // (S116), a last activity at or after activeSince (S107), a reference point (S99, retained when retired,
    // S102.3) inside box, and within scope. Each is read as one snapshot with its profile; one whose stored profile
    // today's rules refuse is left out and logged. Reads in the caller's transaction when there is one, which then
    // holds the profile rows it reads FOR SHARE (IInviteStore.LockPairAsync).
    Task<IReadOnlyList<Climber>> PrefilterAsync(
        Guid requesterId, BoundingBox box, DateTimeOffset activeSince, PrefilterScope scope, CancellationToken cancellationToken);

    // Those of these accounts that have a profile today's rules accept, whatever their state, read as
    // PrefilterAsync reads them.
    Task<IReadOnlyList<Climber>> FindAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
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
        if (await RequesterAsync(requesterId, cancellationToken) is not { } requester)
        {
            return [];
        }

        var climbers = await CandidatesAsync(requester, new PrefilterScope(null, now), now, cancellationToken);

        var gyms = await profiles.FindGymsAsync(
            climbers.Append(requester).SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var activeGyms = gyms.Values.Where(gym => gym.Active).Select(gym => gym.Id).ToHashSet();
        return climbers
            .OrderBy(candidate => CandidateKey.For(requester, candidate, activeGyms), CandidateComparer.Instance)
            .Select(candidate => CardBuilder.For(requester, candidate, gyms))
            .ToList();
    }

    // Design §2 'Invite validation order' step 3: whether the candidate is in the requester's list as computed without
    // S117. The same path as the list, for one candidate: false when either has no profile, or the candidate's is one
    // today's rules refuse; the requester's own refused profile is refused with its code, as on the list.
    public async Task<bool> IsCandidateAsync(Guid requesterId, Guid candidateId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (await RequesterAsync(requesterId, cancellationToken) is not { } requester)
        {
            return false;
        }

        return (await CandidatesAsync(requester, new PrefilterScope(candidateId, null), now, cancellationToken)).Count == 1;
    }

    // The requester as a climber, or null without a profile. A stored profile that today's rules refuse is still the
    // requester's error to fix, with its code, and is logged as RequesterProfileRefused on every path that reads it.
    public async Task<Climber?> RequesterAsync(Guid requesterId, CancellationToken cancellationToken)
    {
        Profile? profile;
        try
        {
            profile = await profiles.FindAsync(requesterId, cancellationToken);
        }
        catch (DomainError error)
        {
            LogOwnProfileRefused(requesterId, error.Code);
            throw;
        }

        return profile is null
            ? null
            : new Climber(requesterId, profile, await PointOfAsync(profile.Postcode, cancellationToken), clock.UtcNow);
    }

    private async Task<List<Climber>> CandidatesAsync(
        Climber requester, PrefilterScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var found = await candidates.PrefilterAsync(
            requester.AccountId,
            BoundingBox.Around(requester.ReferencePoint, requester.Profile.MaxTravelKm + RoundingSlackKm),
            DormancyPolicy.ActiveIfLastActiveAtOrAfter(now),
            scope,
            cancellationToken);
        return found.Where(candidate => CandidateRule.IsCandidate(requester, candidate)).ToList();
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

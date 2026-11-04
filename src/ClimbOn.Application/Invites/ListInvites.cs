using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Invites;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Invites;

// A pending invite to the viewer.
public sealed record ReceivedInvite(Guid Id, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, ClimberCard Sender);

// An invite the viewer sent, in every state, as the sender sees it (S35). Recipient is the recipient's card while the
// invite is pending and their profile is one today's rules accept, otherwise null. DisplayName is null when the
// stored display name is one those rules refuse, and the neutral placeholder (UnnamedRecipientKey) is shown instead.
public sealed record SentInvite(Guid Id, DateTimeOffset SentAt, SentInviteView State, string? DisplayName, ClimberCard? Recipient);

// Newest first.
public sealed record InviteList(IReadOnlyList<ReceivedInvite> Received, IReadOnlyList<SentInvite> Sent);

// The viewer's invites, each pending one with the other climber's card (S54).
public sealed class ListInvites(
    IInviteStore invites, ICandidateStore climbers, IProfileStore profiles, GetSuggestions suggestions, IClock clock)
{
    public const string UnnamedRecipientKey = "invite_recipient_unnamed";

    public async Task<InviteList> RunAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        // The viewer's own stored profile that today's rules refuse is refused with its code, as on the suggestion
        // list; a viewer without a profile has never been anyone's candidate.
        if (await suggestions.RequesterAsync(viewerId, cancellationToken) is not { } viewer)
        {
            return new InviteList([], []);
        }

        var now = clock.UtcNow;
        var all = (await invites.ListAsync(viewerId, now, cancellationToken)).OrderByDescending(invite => invite.SentAt).ToList();
        var received = all.Where(invite => invite.RecipientId == viewerId && invite.IsPendingAt(now)).ToList();
        var sent = all.Where(invite => invite.SenderId == viewerId).ToList();

        // Only a pending invite shows a card (S35, S54), so only its other climber is read whole. A sender whose
        // stored profile today's rules refuse is not listed.
        var carded = received.Select(invite => invite.SenderId)
            .Concat(sent.Where(invite => invite.IsPendingAt(now)).Select(invite => invite.RecipientId))
            .ToHashSet();
        var found = (await climbers.FindAsync(carded, cancellationToken)).ToDictionary(climber => climber.AccountId);
        var gyms = await profiles.FindGymsAsync(
            found.Values.Append(viewer).SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var cards = found.Values.ToDictionary(climber => climber.AccountId, climber => CardBuilder.For(viewer, climber, gyms));

        // S35: a sent invite is never dropped; any other recipient is named as stored when the display name alone
        // passes today's rules.
        var stored = await profiles.FindStoredAsync(
            sent.Select(invite => invite.RecipientId).Where(id => !found.ContainsKey(id)).ToHashSet(), cancellationToken);
        return new InviteList(
            received.Where(invite => cards.ContainsKey(invite.SenderId))
                .Select(invite => new ReceivedInvite(invite.Id, invite.SentAt, invite.ExpiresAt, cards[invite.SenderId]))
                .ToList(),
            sent.Select(invite => new SentInvite(
                    invite.Id,
                    invite.SentAt,
                    invite.SenderViewAt(now),
                    found.TryGetValue(invite.RecipientId, out var recipient)
                        ? recipient.Profile.DisplayName
                        : Profile.ValidDisplayName(stored.GetValueOrDefault(invite.RecipientId)?.DisplayName),
                    invite.IsPendingAt(now) ? cards.GetValueOrDefault(invite.RecipientId) : null))
                .ToList());
    }
}

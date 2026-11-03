using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// A pending invite to the viewer.
public sealed record ReceivedInvite(Guid Id, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, ClimberCard Sender);

// An invite the viewer sent, in every state, as the sender sees it (S35). Recipient is the recipient's card while the
// invite is pending and null once it has ended, when only their display name is shown.
public sealed record SentInvite(Guid Id, DateTimeOffset SentAt, SentInviteView State, string DisplayName, ClimberCard? Recipient);

// Newest first.
public sealed record InviteList(IReadOnlyList<ReceivedInvite> Received, IReadOnlyList<SentInvite> Sent);

// The viewer's invites, each pending one with the other climber's card (S54).
public sealed class ListInvites(
    IInviteStore invites, ICandidateStore climbers, IProfileStore profiles, GetSuggestions suggestions, IClock clock)
{
    public async Task<InviteList> RunAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var all = (await invites.ListAsync(viewerId, now, cancellationToken)).OrderByDescending(invite => invite.SentAt).ToList();
        var received = all.Where(invite => invite.RecipientId == viewerId && invite.IsPendingAt(now)).ToList();
        var sent = all.Where(invite => invite.SenderId == viewerId).ToList();

        // A climber whose stored profile today's rules refuse is not listed. The viewer's own is refused with its
        // code, as on the suggestion list; a viewer without a profile has never been anyone's candidate.
        var others = received.Select(invite => invite.SenderId).Concat(sent.Select(invite => invite.RecipientId)).ToHashSet();
        var found = (await climbers.FindAsync([.. others.Append(viewerId)], cancellationToken))
            .ToDictionary(climber => climber.AccountId);
        if (!found.TryGetValue(viewerId, out var viewer))
        {
            _ = await suggestions.OwnProfileAsync(viewerId, cancellationToken);
            return new InviteList([], []);
        }

        var carded = received.Select(invite => invite.SenderId)
            .Concat(sent.Where(invite => invite.IsPendingAt(now)).Select(invite => invite.RecipientId))
            .Where(found.ContainsKey)
            .ToHashSet();
        var gyms = await profiles.FindGymsAsync(
            carded.Append(viewerId).SelectMany(id => found[id].Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var cards = carded.ToDictionary(id => id, id => CardBuilder.For(viewer, found[id], gyms));
        return new InviteList(
            received.Where(invite => cards.ContainsKey(invite.SenderId))
                .Select(invite => new ReceivedInvite(invite.Id, invite.SentAt, invite.ExpiresAt, cards[invite.SenderId]))
                .ToList(),
            sent.Where(invite => found.ContainsKey(invite.RecipientId))
                .Select(invite => new SentInvite(
                    invite.Id,
                    invite.SentAt,
                    invite.SenderViewAt(now),
                    found[invite.RecipientId].Profile.DisplayName,
                    invite.IsPendingAt(now) ? cards[invite.RecipientId] : null))
                .ToList());
    }
}

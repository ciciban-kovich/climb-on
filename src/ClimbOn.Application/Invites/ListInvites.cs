using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// A pending invite to the viewer.
public sealed record ReceivedInvite(Guid Id, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, ClimberCard Sender);

// An invite the viewer sent, in every state, as the sender sees it (S35).
public sealed record SentInvite(Guid Id, DateTimeOffset SentAt, SentInviteView State, ClimberCard Recipient);

// Newest first.
public sealed record InviteList(IReadOnlyList<ReceivedInvite> Received, IReadOnlyList<SentInvite> Sent);

// The viewer's invites, each with the other climber's card (S54).
public sealed class ListInvites(IInviteStore invites, ICandidateStore climbers, IProfileStore profiles, IClock clock)
{
    public async Task<InviteList> RunAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var all = (await invites.ListAsync(viewerId, cancellationToken)).OrderByDescending(invite => invite.SentAt).ToList();
        var received = all.Where(invite => invite.RecipientId == viewerId && invite.IsPendingAt(now)).ToList();
        var sent = all.Where(invite => invite.SenderId == viewerId).ToList();

        // A climber whose stored profile today's rules refuse has no card, so their invites are not listed; that
        // includes the viewer, who has no profile only if they have never been anyone's candidate.
        var others = received.Select(invite => invite.SenderId).Concat(sent.Select(invite => invite.RecipientId)).ToHashSet();
        var found = (await climbers.FindAsync([.. others.Append(viewerId)], cancellationToken))
            .ToDictionary(climber => climber.AccountId);
        if (!found.TryGetValue(viewerId, out var viewer))
        {
            return new InviteList([], []);
        }

        var gyms = await profiles.FindGymsAsync(
            found.Values.SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var cards = others.Where(found.ContainsKey)
            .ToDictionary(id => id, id => CardBuilder.For(viewer, found[id], gyms));
        return new InviteList(
            received.Where(invite => cards.ContainsKey(invite.SenderId))
                .Select(invite => new ReceivedInvite(invite.Id, invite.SentAt, invite.ExpiresAt, cards[invite.SenderId]))
                .ToList(),
            sent.Where(invite => cards.ContainsKey(invite.RecipientId))
                .Select(invite => new SentInvite(invite.Id, invite.SentAt, invite.SenderViewAt(now), cards[invite.RecipientId]))
                .ToList());
    }
}

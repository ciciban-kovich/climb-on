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
public sealed class ListInvites(IInviteStore invites, ICandidateStore climbers, IProfileStore profiles, IClock clock)
{
    public const string UnnamedRecipientKey = "invite_recipient_unnamed";

    public async Task<InviteList> RunAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var all = (await invites.ListAsync(viewerId, now, cancellationToken)).OrderByDescending(invite => invite.SentAt).ToList();
        var received = all.Where(invite => invite.RecipientId == viewerId && invite.IsPendingAt(now)).ToList();
        var sent = all.Where(invite => invite.SenderId == viewerId).ToList();

        // A sender whose stored profile today's rules refuse is not listed. The viewer's own is refused with its
        // code, as on the suggestion list (ICandidateStore has logged it); a viewer without a profile has never been
        // anyone's candidate.
        var others = received.Select(invite => invite.SenderId).Concat(sent.Select(invite => invite.RecipientId)).ToHashSet();
        var found = (await climbers.FindAsync([.. others.Append(viewerId)], cancellationToken))
            .ToDictionary(climber => climber.AccountId);
        if (!found.TryGetValue(viewerId, out var viewer))
        {
            _ = await profiles.FindAsync(viewerId, cancellationToken);
            return new InviteList([], []);
        }

        var carded = received.Select(invite => invite.SenderId)
            .Concat(sent.Where(invite => invite.IsPendingAt(now)).Select(invite => invite.RecipientId))
            .Where(found.ContainsKey)
            .ToHashSet();
        var gyms = await profiles.FindGymsAsync(
            carded.Append(viewerId).SelectMany(id => found[id].Profile.HomeGyms).Distinct().ToList(), cancellationToken);
        var cards = carded.ToDictionary(id => id, id => CardBuilder.For(viewer, found[id], gyms));

        // S35: a sent invite is never dropped; a recipient whose stored profile is refused is named as stored when
        // the display name alone passes today's rules.
        var refused = await profiles.FindStoredAsync(
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
                        : Profile.ValidDisplayName(refused.GetValueOrDefault(invite.RecipientId)?.DisplayName),
                    invite.IsPendingAt(now) ? cards.GetValueOrDefault(invite.RecipientId) : null))
                .ToList());
    }
}

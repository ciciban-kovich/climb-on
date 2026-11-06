using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Notifications;
using ClimbOn.Application.Outbox;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// The recipient accepts a pending invite, which creates a connection and notifies its sender (S69.2), or declines it
// (S7), which notifies nobody.
public sealed class AnswerInvite(IAccountStore accounts, IInviteStore invites, OutboxQueue outbox, IClock clock)
{
    public const string NotFoundCode = "invite_not_found";

    public static IReadOnlyList<string> Codes { get; } =
        [NotFoundCode, Invite.NotPendingCode, GetSuggestions.EmailNotConfirmedCode, SafetyNotice.NotAcknowledgedCode];

    public Task AcceptAsync(Guid recipientId, Guid inviteId, CancellationToken cancellationToken) =>
        AnswerAsync(recipientId, inviteId, accept: true, cancellationToken);

    public Task DeclineAsync(Guid recipientId, Guid inviteId, CancellationToken cancellationToken) =>
        AnswerAsync(recipientId, inviteId, accept: false, cancellationToken);

    private async Task AnswerAsync(Guid recipientId, Guid inviteId, bool accept, CancellationToken cancellationToken)
    {
        // The account gates, in sending's order: S2, then S23, which gates accepting but not declining.
        if (await accounts.FindOwnAsync(recipientId, cancellationToken) is not { EmailConfirmed: true })
        {
            throw new DomainError(GetSuggestions.EmailNotConfirmedCode, DomainErrorKind.Forbidden);
        }

        if (accept && await invites.FindSafetyAcknowledgementAsync(recipientId, cancellationToken) is null)
        {
            throw new DomainError(SafetyNotice.NotAcknowledgedCode, DomainErrorKind.Forbidden);
        }

        // Only its recipient can answer an invite; to anyone else it does not exist.
        if (await invites.FindAsync(inviteId, cancellationToken) is not { } found || found.RecipientId != recipientId)
        {
            throw new DomainError(NotFoundCode, DomainErrorKind.NotFound);
        }

        var now = clock.UtcNow;
        await using var pair = await invites.LockPairAsync(recipientId, found.SenderId, PairAction.Answer, now, cancellationToken);

        // Both accounts' state again, under the locks.
        if (pair.Actor is not { EmailConfirmed: true })
        {
            throw new DomainError(GetSuggestions.EmailNotConfirmedCode, DomainErrorKind.Forbidden);
        }

        // The invite as it is under the locks: another request may have answered it, or S34 accepted it.
        var invite = pair.Invites.Single(invite => invite.Id == inviteId);
        if (pair.Other is not { EmailConfirmed: true })
        {
            throw new DomainError(Invite.NotPendingCode, DomainErrorKind.Conflict);
        }

        Guid? accepted = null;
        if (accept)
        {
            var answered = invite.Accept(now);
            var connectionId = Guid.NewGuid();

            // Enqueued first, so that the pair's write saves it in the same transaction.
            accepted = outbox.Enqueue(
                new NotificationEvent(NotificationKind.InviteAccepted, invite.SenderId, recipientId, connectionId));
            await pair.AcceptAsync(answered, connectionId, cancellationToken);
        }
        else
        {
            await pair.DeclineAsync(invite.Decline(now), cancellationToken);
        }

        await pair.CommitAsync(cancellationToken);
        if (accepted is { } id)
        {
            await outbox.DeliverAsync(id, cancellationToken);
        }
    }
}

using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// The recipient accepts a pending invite, which creates a connection, or declines it (S7).
public sealed class AnswerInvite(IInviteStore invites, IClock clock)
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
        // S23, an account gate like sending's: accepting needs the acknowledgement, declining does not.
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

        // S2, under the locks: an unconfirmed account cannot receive an invite, so none to it is pending.
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

        if (accept)
        {
            await pair.AcceptAsync(invite.Accept(now), cancellationToken);
        }
        else
        {
            await pair.DeclineAsync(invite.Decline(now), cancellationToken);
        }

        await pair.CommitAsync(cancellationToken);
    }
}

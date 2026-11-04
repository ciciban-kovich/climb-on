using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Invites;

namespace ClimbOn.Application.Invites;

// What an invite request did: created an invite, or accepted the recipient's pending invite instead (S34).
public sealed record InviteOutcome(Guid? InviteId, bool Connected);

// S5, S2, S23, S31-S34, in the order of design §2 'Invite validation order'.
public sealed class SendInvite(
    IAccountStore accounts, IInviteStore invites, GetSuggestions suggestions, IClock clock)
{
    public static IReadOnlyList<string> Codes { get; } =
        [GetSuggestions.EmailNotConfirmedCode, SafetyNotice.NotAcknowledgedCode, .. InvitePolicy.Codes];

    public async Task<InviteOutcome> RunAsync(Guid senderId, Guid recipientId, CancellationToken cancellationToken)
    {
        // Step 1, the account gates: S2 for the sender, then S23.
        if (await accounts.FindOwnAsync(senderId, cancellationToken) is not { EmailConfirmed: true })
        {
            throw new DomainError(GetSuggestions.EmailNotConfirmedCode, DomainErrorKind.Forbidden);
        }

        if (await invites.FindSafetyAcknowledgementAsync(senderId, cancellationToken) is null)
        {
            throw new DomainError(SafetyNotice.NotAcknowledgedCode, DomainErrorKind.Forbidden);
        }

        // No climber is their own candidate, and a pair needs two accounts to lock.
        if (recipientId == senderId)
        {
            throw new DomainError(InvitePolicy.NotCandidateCode, DomainErrorKind.Conflict);
        }

        var now = clock.UtcNow;
        await using var pair = await invites.LockPairAsync(senderId, recipientId, PairAction.Invite, now, cancellationToken);

        // Both accounts' state again, under the locks. S2 for the recipient: an unconfirmed one is no candidate
        // (S116), and is refused as one.
        if (pair.Actor is not { EmailConfirmed: true })
        {
            throw new DomainError(GetSuggestions.EmailNotConfirmedCode, DomainErrorKind.Forbidden);
        }

        if (pair.Other is not { EmailConfirmed: true })
        {
            throw new DomainError(InvitePolicy.NotCandidateCode, DomainErrorKind.Conflict);
        }

        // Step 2, S34.
        if (InvitePolicy.ToAcceptInstead(senderId, recipientId, pair.Invites, now) is { } reverse)
        {
            await pair.AcceptAsync(reverse.Accept(now), cancellationToken);
            await pair.CommitAsync(cancellationToken);
            return new InviteOutcome(null, Connected: true);
        }

        // Steps 3 to 5.
        InvitePolicy.EnsureMayInvite(
            senderId,
            recipientId,
            await suggestions.IsCandidateAsync(senderId, recipientId, cancellationToken),
            pair.Connected,
            pair.Invites,
            await pair.CountPendingSentAsync(now, cancellationToken),
            now);

        var invite = Invite.Send(Guid.NewGuid(), senderId, recipientId, now);
        await pair.AddAsync(invite, cancellationToken);
        await pair.CommitAsync(cancellationToken);
        return new InviteOutcome(invite.Id, Connected: false);
    }
}

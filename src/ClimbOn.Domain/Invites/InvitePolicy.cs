using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Invites;

// Design §2 'Invite validation order', the steps after the account gates: S34 first, then the candidate check, the
// connection check, then S32, S33 and S31. pairInvites are every invite between the sender and the recipient, in
// either direction.
public static class InvitePolicy
{
    public const string NotCandidateCode = "invite_not_candidate";
    public const string AlreadyConnectedCode = "invite_already_connected";
    public const string AlreadyPendingCode = "invite_already_pending";
    public const string CooldownCode = "invite_cooldown";
    public const string LimitReachedCode = "invite_limit_reached";

    // S31
    public const int MaxPendingSent = 10;

    // S33
    public static readonly TimeSpan Cooldown = TimeSpan.FromDays(30);

    public static IReadOnlyList<string> Codes { get; } =
        [NotCandidateCode, AlreadyConnectedCode, AlreadyPendingCode, CooldownCode, LimitReachedCode, Invite.NotPendingCode];

    // S34: the pending invite from the recipient to the sender that this invite accepts instead; null when there is
    // none.
    public static Invite? ToAcceptInstead(Guid senderId, Guid recipientId, IEnumerable<Invite> pairInvites, DateTimeOffset now) =>
        pairInvites.FirstOrDefault(invite =>
            invite.SenderId == recipientId && invite.RecipientId == senderId && invite.IsPendingAt(now));

    // The steps after S34, in order; throws the first rule's DomainError that refuses the invite. pendingSent is the
    // number of the sender's outgoing invites pending at now.
    public static void EnsureMayInvite(
        Guid senderId,
        Guid recipientId,
        bool isCandidate,
        bool connected,
        IEnumerable<Invite> pairInvites,
        int pendingSent,
        DateTimeOffset now)
    {
        if (!isCandidate)
        {
            throw new DomainError(NotCandidateCode, DomainErrorKind.Conflict);
        }

        if (connected)
        {
            throw new DomainError(AlreadyConnectedCode, DomainErrorKind.Conflict);
        }

        var sent = pairInvites.Where(invite => invite.SenderId == senderId && invite.RecipientId == recipientId).ToList();
        if (sent.Any(invite => invite.IsPendingAt(now)))
        {
            throw new DomainError(AlreadyPendingCode, DomainErrorKind.Conflict);
        }

        if (sent.Any(invite => CoolsDownAt(invite, now)))
        {
            throw new DomainError(CooldownCode, DomainErrorKind.Conflict);
        }

        if (pendingSent >= MaxPendingSent)
        {
            throw new DomainError(LimitReachedCode, DomainErrorKind.Conflict);
        }
    }

    // S33: a declined invite, or one that expired for any cause but deletion (S37), bars its sender for Cooldown
    // after it ended. The recipient is never barred by it.
    public static bool CoolsDownAt(Invite invite, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invite);
        var starts = invite.StateAt(now) switch
        {
            InviteState.Declined => true,
            InviteState.Expired => invite.CauseAt(now) != ExpiryCause.Deletion,
            _ => false,
        };
        return starts && now < invite.EndedAtTime(now) + Cooldown;
    }
}

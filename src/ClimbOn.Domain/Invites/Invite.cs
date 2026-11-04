using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Invites;

// S7
public enum InviteState
{
    Pending,
    Accepted,
    Declined,
    Expired,
}

// Why a stored invite expired (design §3 'Invite expiry cause'). Deletion (S37), block (S24.2) and suspension (S26)
// arrive in M2.
public enum ExpiryCause
{
    Timeout,
    Deletion,
    Block,
    Suspension,
}

// What the sender sees (S35): a declined and an expired invite are both NotAccepted.
public enum SentInviteView
{
    Pending,
    Accepted,
    NotAccepted,
}

// An invite as stored. A stored pending invite is expired by timeout once Lifetime has passed since it was sent (S8),
// whether or not that has been written yet (design §3 'Time-derived states'). EndedAt is when it left pending, and
// Cause is set exactly when StoredState is Expired.
public sealed record Invite(
    Guid Id,
    Guid SenderId,
    Guid RecipientId,
    DateTimeOffset SentAt,
    InviteState StoredState,
    ExpiryCause? Cause,
    DateTimeOffset? EndedAt)
{
    public const string NotPendingCode = "invite_not_pending";

    // S8
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public DateTimeOffset ExpiresAt => SentAt + Lifetime;

    public static Invite Send(Guid id, Guid senderId, Guid recipientId, DateTimeOffset now) =>
        new(id, senderId, recipientId, now, InviteState.Pending, null, null);

    // S7, S8
    public InviteState StateAt(DateTimeOffset now) =>
        StoredState == InviteState.Pending && now >= ExpiresAt ? InviteState.Expired : StoredState;

    public ExpiryCause? CauseAt(DateTimeOffset now) =>
        StoredState == InviteState.Pending && now >= ExpiresAt ? ExpiryCause.Timeout : Cause;

    public DateTimeOffset? EndedAtTime(DateTimeOffset now) =>
        StoredState == InviteState.Pending && now >= ExpiresAt ? ExpiresAt : EndedAt;

    public bool IsPendingAt(DateTimeOffset now) => StateAt(now) == InviteState.Pending;

    // S35
    public SentInviteView SenderViewAt(DateTimeOffset now) => StateAt(now) switch
    {
        InviteState.Pending => SentInviteView.Pending,
        InviteState.Accepted => SentInviteView.Accepted,
        _ => SentInviteView.NotAccepted,
    };

    // The state the timeout has already brought about, as it is written (design §4.2); unchanged otherwise.
    public Invite Materialised(DateTimeOffset now) =>
        StoredState == InviteState.Pending && now >= ExpiresAt
            ? this with { StoredState = InviteState.Expired, Cause = ExpiryCause.Timeout, EndedAt = ExpiresAt }
            : this;

    // S7: only a pending invite can be accepted or declined; the other states are final.
    public Invite Accept(DateTimeOffset now) => End(InviteState.Accepted, now);

    public Invite Decline(DateTimeOffset now) => End(InviteState.Declined, now);

    private Invite End(InviteState state, DateTimeOffset now) =>
        IsPendingAt(now)
            ? this with { StoredState = state, EndedAt = now }
            : throw new DomainError(NotPendingCode, DomainErrorKind.Conflict);
}

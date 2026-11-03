using ClimbOn.Domain.Invites;

namespace ClimbOn.Infrastructure.Persistence.Invites;

// A row of invites. State and ExpiryCause hold the wire names in InviteNames.
public sealed class InviteRow
{
    public required Guid Id { get; init; }

    public required Guid SenderId { get; init; }

    public required Guid RecipientId { get; init; }

    public required DateTimeOffset SentAt { get; init; }

    public required string State { get; set; }

    // Set exactly when State is expired.
    public string? ExpiryCause { get; set; }

    // When the invite left pending; null while it is pending in storage.
    public DateTimeOffset? EndedAt { get; set; }

    public Invite ToInvite() => new(
        Id, SenderId, RecipientId, SentAt, InviteNames.ParseState(State), ExpiryCause is null ? null : InviteNames.ParseCause(ExpiryCause), EndedAt);
}

// A row of connections. PairLow is the lower of the two account IDs in PostgreSQL's uuid order (PairLocks.Order).
public sealed class ConnectionRow
{
    public required Guid Id { get; init; }

    public required Guid PairLow { get; init; }

    public required Guid PairHigh { get; init; }

    // active, paused or ended (specs glossary); pausing and ending arrive in M2.
    public required string State { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    // The accepted invite the connection was created by.
    public required Guid InviteId { get; init; }
}

// A row of safety_acknowledgements (S23).
public sealed class SafetyAcknowledgementRow
{
    public required Guid AccountId { get; init; }

    public required DateTimeOffset AcknowledgedAt { get; init; }
}

public static class InviteNames
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Expired = "expired";

    public const string ConnectionActive = "active";
    public const string ConnectionPaused = "paused";
    public const string ConnectionEnded = "ended";

    public static string Of(InviteState state) => state switch
    {
        InviteState.Pending => Pending,
        InviteState.Accepted => Accepted,
        InviteState.Declined => Declined,
        InviteState.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Of(ExpiryCause cause) => cause switch
    {
        ExpiryCause.Timeout => "timeout",
        ExpiryCause.Deletion => "deletion",
        ExpiryCause.Block => "block",
        ExpiryCause.Suspension => "suspension",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };

    public static InviteState ParseState(string state) =>
        Enum.GetValues<InviteState>().Single(value => Of(value) == state);

    public static ExpiryCause ParseCause(string cause) =>
        Enum.GetValues<ExpiryCause>().Single(value => Of(value) == cause);
}

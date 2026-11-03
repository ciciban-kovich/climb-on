using ClimbOn.Application.Abstractions;

namespace ClimbOn.Application.Invites;

// S23: the safety notice, and its acknowledgement.
public sealed class SafetyNotice(IInviteStore store, IClock clock)
{
    // The text key of the notice; the api returns it in the account's language (S105).
    public const string TextKey = "safety_notice_text";

    public const string NotAcknowledgedCode = "safety_notice_not_acknowledged";

    public Task<DateTimeOffset?> AcknowledgedAtAsync(Guid accountId, CancellationToken cancellationToken) =>
        store.FindSafetyAcknowledgementAsync(accountId, cancellationToken);

    // Idempotent: a second acknowledgement keeps the first's time.
    public Task<DateTimeOffset> AcknowledgeAsync(Guid accountId, CancellationToken cancellationToken) =>
        store.AcknowledgeSafetyNoticeAsync(accountId, clock.UtcNow, cancellationToken);
}

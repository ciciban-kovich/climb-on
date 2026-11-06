using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Notifications;

// S69
public enum NotificationKind
{
    InviteReceived,
    InviteAccepted,
    NewMessage,
}

// A notification event for one recipient. Actor is the other climber, the one the notification names (S74). Subject
// is what it is about: the invite for InviteReceived, the connection for InviteAccepted and NewMessage. It never
// holds message content (S74).
public sealed record NotificationEvent(NotificationKind Kind, Guid RecipientId, Guid ActorId, Guid SubjectId);

// An in-app notification (S70) as stored. Its ID is the ID of the outbox row that delivered it.
public sealed record InAppNotification(
    Guid Id, NotificationKind Kind, Guid ActorId, Guid SubjectId, DateTimeOffset CreatedAt, DateTimeOffset? SeenAt);

public interface INotificationStore
{
    // Writes the in-app notification, created at occurredAt, in the transaction open on this scope's database context
    // (the outbox claim's), so that it commits exactly when the claim is marked sent. A second write of one ID is
    // ignored.
    Task AddInAppAsync(Guid id, NotificationEvent notification, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    // The account's newest notifications, newest first, at most size of them.
    Task<IReadOnlyList<InAppNotification>> ListAsync(Guid accountId, int size, CancellationToken cancellationToken);

    // Sets SeenAt to now unless it is set. False when the account has no notification with this ID.
    Task<bool> MarkSeenAsync(Guid accountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}

// Design §4.1: an event reaches its channels through the outbox. In M1 the only channel is in-app (S70); the outbox
// delivers the event by calling this inside the claim's transaction.
public sealed class NotificationFanOut(INotificationStore store)
{
    // outboxId is the delivering row's ID, which the in-app notification takes as its own.
    public Task DeliverAsync(
        Guid outboxId, NotificationEvent notification, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
        store.AddInAppAsync(outboxId, notification, occurredAt, cancellationToken);
}

// A notification as its recipient sees it. DisplayName is the actor's stored display name when it passes today's
// rules, otherwise null, and the neutral placeholder (UnnamedActorKey) is shown instead.
public sealed record NotificationEntry(InAppNotification Notification, string? DisplayName);

// The climber's in-app notifications (S70).
public sealed class ClimberNotifications(INotificationStore store, IProfileStore profiles, IClock clock)
{
    public const string NotFoundCode = "notification_not_found";
    public const string UnnamedActorKey = "notification_actor_unnamed";

    // The newest ones the list returns.
    public const int ListSize = 50;

    public static IReadOnlyList<string> Codes { get; } = [NotFoundCode];

    public async Task<IReadOnlyList<NotificationEntry>> ListAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var notifications = await store.ListAsync(accountId, ListSize, cancellationToken);
        if (notifications.Count == 0)
        {
            return [];
        }

        var stored = await profiles.FindStoredAsync(
            notifications.Select(notification => notification.ActorId).ToHashSet(), cancellationToken);
        return notifications
            .Select(notification => new NotificationEntry(
                notification, Profile.ValidDisplayName(stored.GetValueOrDefault(notification.ActorId)?.DisplayName)))
            .ToList();
    }

    public async Task MarkSeenAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        if (!await store.MarkSeenAsync(accountId, id, clock.UtcNow, cancellationToken))
        {
            throw new DomainError(NotFoundCode, DomainErrorKind.NotFound);
        }
    }
}

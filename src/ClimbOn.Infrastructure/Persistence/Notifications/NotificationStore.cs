using ClimbOn.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Notifications;

// A row of notifications: an in-app notification (S70). It holds no message content (S74).
public sealed class NotificationRow
{
    public required Guid Id { get; init; }

    // The recipient.
    public required Guid AccountId { get; init; }

    // A wire name in NotificationNames.
    public required string Event { get; init; }

    public required Guid ActorId { get; init; }

    public required Guid SubjectId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SeenAt { get; init; }

    public InAppNotification ToNotification() =>
        new(Id, NotificationNames.Parse(Event), ActorId, SubjectId, CreatedAt, SeenAt);
}

public static class NotificationNames
{
    public const string InviteReceived = "invite-received";
    public const string InviteAccepted = "invite-accepted";
    public const string NewMessage = "new-message";

    public static string Of(NotificationKind kind) => kind switch
    {
        NotificationKind.InviteReceived => InviteReceived,
        NotificationKind.InviteAccepted => InviteAccepted,
        NotificationKind.NewMessage => NewMessage,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static NotificationKind Parse(string name) =>
        Enum.GetValues<NotificationKind>().Single(kind => Of(kind) == name);
}

public sealed class NotificationStore(ClimbOnDbContext db) : INotificationStore
{
    // One statement on the context's connection, so it runs in the transaction open there, and touches nothing the
    // change tracker holds.
    public Task AddInAppAsync(
        Guid id, NotificationEvent notification, DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notifications (id, account_id, event, actor_id, subject_id, created_at)
            VALUES ({id}, {notification.RecipientId}, {NotificationNames.Of(notification.Kind)}, {notification.ActorId},
                    {notification.SubjectId}, {occurredAt})
            ON CONFLICT (id) DO NOTHING
            """,
            cancellationToken);

    public async Task<IReadOnlyList<InAppNotification>> ListAsync(Guid accountId, int size, CancellationToken cancellationToken) =>
        (await db.Notifications.AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Take(size)
            .ToListAsync(cancellationToken))
        .Select(row => row.ToNotification())
        .ToList();

    public async Task<bool> MarkSeenAsync(Guid accountId, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Notifications
                .Where(row => row.Id == id && row.AccountId == accountId && row.SeenAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.SeenAt, now), cancellationToken) == 1)
        {
            return true;
        }

        return await db.Notifications.AnyAsync(row => row.Id == id && row.AccountId == accountId, cancellationToken);
    }
}

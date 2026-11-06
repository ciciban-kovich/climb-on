using System.Globalization;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Notifications;
using ClimbOn.Domain.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// Event is invite-received, invite-accepted or new-message (S69). Text says what happened in the viewer's language
// (S105), naming the other climber and nothing more (S74). DisplayName is that climber's display name, or a neutral
// placeholder when their stored one is refused. SubjectId is the invite (invite-received) or the connection
// (invite-accepted, new-message).
public sealed record NotificationResponse(
    Guid Id, string Event, string Text, string DisplayName, Guid SubjectId, DateTimeOffset CreatedAt, bool Seen);

// The newest 50, newest first.
public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Notifications);

public static class NotificationEndpoints
{
    private const string Tag = "Notifications";
    private const string Route = "/api/climber/notifications";

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, ListAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost(Route + "/{id:guid}/seen", MarkSeenAsync)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    public static string WireName(NotificationKind kind) => kind switch
    {
        NotificationKind.InviteReceived => "invite-received",
        NotificationKind.InviteAccepted => "invite-accepted",
        NotificationKind.NewMessage => "new-message",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // The text of each event, which takes the display name as {0}.
    public static string TextKey(NotificationKind kind) => kind switch
    {
        NotificationKind.InviteReceived => "notification_invite_received",
        NotificationKind.InviteAccepted => "notification_invite_accepted",
        NotificationKind.NewMessage => "notification_new_message",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static async Task<Ok<NotificationListResponse>> ListAsync(
        HttpContext context, ClimberNotifications notifications, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        var entries = await notifications.ListAsync(session.AccountId, cancellationToken);
        return TypedResults.Ok(new NotificationListResponse(entries.Select(entry =>
        {
            var notification = entry.Notification;
            var name = entry.DisplayName ?? Texts.Get(ClimberNotifications.UnnamedActorKey, session.Language);
            return new NotificationResponse(
                notification.Id,
                WireName(notification.Kind),
                string.Format(CultureInfo.GetCultureInfo(session.Language.Tag()), Texts.Get(TextKey(notification.Kind), session.Language), name),
                name,
                notification.SubjectId,
                notification.CreatedAt,
                notification.SeenAt is not null);
        }).ToList()));
    }

    private static async Task<NoContent> MarkSeenAsync(
        Guid id, HttpContext context, ClimberNotifications notifications, CancellationToken cancellationToken)
    {
        await notifications.MarkSeenAsync(ClimberSessions.Current(context).AccountId, id, cancellationToken);
        return TypedResults.NoContent();
    }
}

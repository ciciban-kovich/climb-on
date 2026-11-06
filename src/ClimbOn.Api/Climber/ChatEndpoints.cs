using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Chat;
using ClimbOn.Domain.Chat;
using ClimbOn.Domain.Invites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// State is active, paused or ended. Partner is the other climber's card (S54), null when their stored profile is one
// today's rules refuse; DisplayName is then a neutral placeholder unless the stored display name passes those rules.
public sealed record ConnectionResponse(
    Guid Id, string State, DateTimeOffset CreatedAt, string DisplayName, ClimberCardResponse? Partner);

// Newest first.
public sealed record ConnectionListResponse(IReadOnlyList<ConnectionResponse> Connections);

public sealed record MessageRequest(string? Text);

// Text exactly as written, as plain text (S115). Mine is whether the viewer sent it. No read state (N18).
public sealed record MessageResponse(Guid Id, bool Mine, string Text, DateTimeOffset SentAt);

// A page of at most 10 messages, in the order they were sent (S118). HasOlder is whether messages before the first
// one exist; ask for them with before set to its Id.
public sealed record MessageListResponse(IReadOnlyList<MessageResponse> Messages, bool HasOlder);

public static class ChatEndpoints
{
    private const string Tag = "Chat";
    private const string Route = "/api/climber/connections";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, ListConnectionsAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapGet(Route + "/{id:guid}/messages", ListMessagesAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost(Route + "/{id:guid}/messages", SendAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .Produces<ErrorBody>(StatusCodes.Status409Conflict)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    public static string WireName(ConnectionState state) => state switch
    {
        ConnectionState.Active => "active",
        ConnectionState.Paused => "paused",
        ConnectionState.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static async Task<Ok<ConnectionListResponse>> ListConnectionsAsync(
        HttpContext context, ListConnections list, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        var connections = await list.RunAsync(session.AccountId, cancellationToken);
        return TypedResults.Ok(new ConnectionListResponse(connections.Select(entry => new ConnectionResponse(
            entry.Connection.Id,
            WireName(entry.Connection.State),
            entry.Connection.CreatedAt,
            entry.DisplayName ?? Texts.Get(ListConnections.UnnamedPartnerKey, session.Language),
            entry.Partner is { } card ? SuggestionEndpoints.ToResponse(card, session.Language) : null)).ToList()));
    }

    private static async Task<Ok<MessageListResponse>> ListMessagesAsync(
        Guid id, string? before, HttpContext context, Messages messages, CancellationToken cancellationToken)
    {
        var viewerId = ClimberSessions.Current(context).AccountId;
        var page = await messages.ListAsync(viewerId, id, before, cancellationToken);
        return TypedResults.Ok(new MessageListResponse(
            page.Messages.Select(message => ToResponse(message, viewerId)).ToList(), page.HasOlder));
    }

    private static async Task<Ok<MessageResponse>> SendAsync(
        Guid id, MessageRequest request, HttpContext context, Messages messages, CancellationToken cancellationToken)
    {
        var senderId = ClimberSessions.Current(context).AccountId;
        return TypedResults.Ok(ToResponse(await messages.SendAsync(senderId, id, request.Text, cancellationToken), senderId));
    }

    private static MessageResponse ToResponse(Message message, Guid viewerId) =>
        new(message.Id, message.SenderId == viewerId, message.Text, message.SentAt);
}

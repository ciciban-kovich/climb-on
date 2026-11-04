using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Invites;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Invites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// S23: Text is in the account's language (S105); AcknowledgedAt is null until the climber acknowledges it.
public sealed record SafetyNoticeResponse(string Text, DateTimeOffset? AcknowledgedAt);

// RecipientId is the AccountId of a card in the sender's suggestion list (S5).
public sealed record InviteRequest(Guid? RecipientId);

// Outcome is invited, with the new invite's ID, or connected when the request accepted the recipient's pending
// invite instead (S34).
public sealed record InviteResultResponse(string Outcome, Guid? InviteId);

public sealed record ReceivedInviteResponse(Guid Id, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, ClimberCardResponse Sender);

// State is pending, accepted or not-accepted (S35). Recipient is the recipient's card while State is pending and
// their profile is valid, otherwise null. DisplayName is a neutral placeholder when the stored one is refused.
public sealed record SentInviteResponse(
    Guid Id, DateTimeOffset SentAt, string State, string DisplayName, ClimberCardResponse? Recipient);

// Received holds the pending invites to the climber, Sent every invite they sent; both newest first.
public sealed record InviteListResponse(IReadOnlyList<ReceivedInviteResponse> Received, IReadOnlyList<SentInviteResponse> Sent);

public static class InviteEndpoints
{
    public const string Invited = "invited";
    public const string Connected = "connected";

    private const string Tag = "Invites";
    private const string Route = "/api/climber/invites";

    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/climber/safety-notice", GetSafetyNoticeAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost("/api/climber/safety-notice/acknowledgement", AcknowledgeSafetyNoticeAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapGet(Route, ListAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost(Route, SendAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<ErrorBody>(StatusCodes.Status403Forbidden)
            .Produces<ErrorBody>(StatusCodes.Status409Conflict)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost(Route + "/{id:guid}/accept", AcceptAsync)
            .Produces<ErrorBody>(StatusCodes.Status403Forbidden)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .Produces<ErrorBody>(StatusCodes.Status409Conflict)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapPost(Route + "/{id:guid}/decline", DeclineAsync)
            .Produces<ErrorBody>(StatusCodes.Status403Forbidden)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .Produces<ErrorBody>(StatusCodes.Status409Conflict)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    public static string WireName(SentInviteView view) => view switch
    {
        SentInviteView.Pending => "pending",
        SentInviteView.Accepted => "accepted",
        SentInviteView.NotAccepted => "not-accepted",
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
    };

    private static async Task<Ok<SafetyNoticeResponse>> GetSafetyNoticeAsync(
        HttpContext context, SafetyNotice notice, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        return TypedResults.Ok(new SafetyNoticeResponse(
            Texts.Get(SafetyNotice.TextKey, session.Language),
            await notice.AcknowledgedAtAsync(session.AccountId, cancellationToken)));
    }

    private static async Task<Ok<SafetyNoticeResponse>> AcknowledgeSafetyNoticeAsync(
        HttpContext context, SafetyNotice notice, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        return TypedResults.Ok(new SafetyNoticeResponse(
            Texts.Get(SafetyNotice.TextKey, session.Language),
            await notice.AcknowledgeAsync(session.AccountId, cancellationToken)));
    }

    private static async Task<Ok<InviteListResponse>> ListAsync(
        HttpContext context, ListInvites list, CancellationToken cancellationToken)
    {
        var session = ClimberSessions.Current(context);
        var invites = await list.RunAsync(session.AccountId, cancellationToken);
        return TypedResults.Ok(new InviteListResponse(
            invites.Received.Select(invite => new ReceivedInviteResponse(
                invite.Id, invite.SentAt, invite.ExpiresAt, SuggestionEndpoints.ToResponse(invite.Sender, session.Language))).ToList(),
            invites.Sent.Select(invite => new SentInviteResponse(
                invite.Id,
                invite.SentAt,
                WireName(invite.State),
                invite.DisplayName ?? Texts.Get(ListInvites.UnnamedRecipientKey, session.Language),
                invite.Recipient is { } card ? SuggestionEndpoints.ToResponse(card, session.Language) : null)).ToList()));
    }

    private static async Task<Ok<InviteResultResponse>> SendAsync(
        InviteRequest request, HttpContext context, SendInvite send, CancellationToken cancellationToken)
    {
        if (request.RecipientId is not { } recipientId)
        {
            throw new DomainError(ErrorCodes.ValidationFailed);
        }

        var outcome = await send.RunAsync(ClimberSessions.Current(context).AccountId, recipientId, cancellationToken);
        return TypedResults.Ok(new InviteResultResponse(outcome.Connected ? Connected : Invited, outcome.InviteId));
    }

    private static async Task<NoContent> AcceptAsync(
        Guid id, HttpContext context, AnswerInvite answer, CancellationToken cancellationToken)
    {
        await answer.AcceptAsync(ClimberSessions.Current(context).AccountId, id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeclineAsync(
        Guid id, HttpContext context, AnswerInvite answer, CancellationToken cancellationToken)
    {
        await answer.DeclineAsync(ClimberSessions.Current(context).AccountId, id, cancellationToken);
        return TypedResults.NoContent();
    }
}

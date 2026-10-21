using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

public sealed record LoginRequest(string? Email, string? Password);

// The logged-in climber's own account. Language is the stored one, en or nb (S104).
public sealed record MeResponse(string Email, string Language, bool EmailConfirmed);

public static class SessionEndpoints
{
    private const string Tag = "Sessions";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // S78: the session travels only in its cookie, never in a body.
        endpoints.MapPost("/api/climber/login", LoginAsync)
            .Produces<ErrorBody>(StatusCodes.Status401Unauthorized)
            .WithTags(Tag);
        endpoints.MapPost("/api/climber/logout", LogoutAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        endpoints.MapGet("/api/climber/me", MeAsync)
            .RequireClimberSession()
            .WithTags(Tag);
        return endpoints;
    }

    private static async Task<NoContent> LoginAsync(
        LoginRequest request, Login login, HttpContext context, CancellationToken cancellationToken)
    {
        var session = await login.RunAsync(request.Email, request.Password, cancellationToken);
        SessionCookie.Write(context.Response, session.Token, session.ExpiresAt);
        return TypedResults.NoContent();
    }

    // S82: ends the session the cookie names.
    private static async Task<NoContent> LogoutAsync(HttpContext context, Sessions sessions, CancellationToken cancellationToken)
    {
        if (SessionCookie.Read(context.Request) is { } token)
        {
            await sessions.EndAsync(token, cancellationToken);
        }

        SessionCookie.Delete(context.Response);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MeResponse>> MeAsync(HttpContext context, IAccountStore accounts, CancellationToken cancellationToken)
    {
        // An account erased since its session resolved has no session any more either.
        var account = await accounts.FindOwnAsync(ClimberSessions.Current(context).AccountId, cancellationToken)
            ?? throw new DomainError(ClimberSessions.RequiredCode, DomainErrorKind.Unauthenticated);
        return TypedResults.Ok(new MeResponse(account.Email, account.Language.Tag(), account.EmailConfirmed));
    }
}

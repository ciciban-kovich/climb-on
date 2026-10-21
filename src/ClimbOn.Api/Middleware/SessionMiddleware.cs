using ClimbOn.Api.Errors;
using ClimbOn.Application.Accounts;

namespace ClimbOn.Api.Middleware;

// Marks an endpoint that only a logged-in climber may call.
public sealed class ClimberSessionRequired;

// Set on the request once its session has resolved.
public sealed record ClimberSessionFeature(ClimberSession Session);

public static class ClimberSessions
{
    public const string RequiredCode = ErrorCodes.SessionRequired;

    public static RouteHandlerBuilder RequireClimberSession(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new ClimberSessionRequired()).Produces<ErrorBody>(StatusCodes.Status401Unauthorized);

    // The session of an endpoint that requires one.
    public static ClimberSession Current(HttpContext context) =>
        context.Features.Get<ClimberSessionFeature>()?.Session
            ?? throw new InvalidOperationException("The endpoint does not require a climber session.");
}

// The session cookie (design §2 'Runtime components'). __Host- binds it to this origin, path / and HTTPS.
public static class SessionCookie
{
    public const string Name = "__Host-climbon-session";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrEmpty(token) ? token : null;

    // Expires with the session (S82), so the browser keeps it as long as the server does.
    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    public static bool IsSet(HttpResponse response) =>
        response.Headers.SetCookie.Any(value => value?.StartsWith(Name + "=", StringComparison.Ordinal) == true);

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
        Expires = expiresAt,
    };
}

// Design §2 'Request pipeline', first step: the session cookie gives the climber. On every climber API request
// that resolves a session, the account's last activity and the session's last use are written and committed
// before the endpoint runs (Sessions.ResumeAsync), and errors follow the account's stored language (S105).
// That includes endpoints that need no session and the 405 and 415 endpoints routing answers with itself.
public sealed class SessionMiddleware(RequestDelegate next)
{
    private const string ClimberApiPrefix = "/api/climber";

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var required = endpoint?.Metadata.GetMetadata<ClimberSessionRequired>() is not null;
        if (endpoint is null || (!required && !context.Request.Path.StartsWithSegments(ClimberApiPrefix)))
        {
            await next(context);
            return;
        }

        // S98: the climber API accepts climber sessions only, never a bearer token or other credentials.
        if (context.Request.Headers.Authorization.Count > 0)
        {
            await RefuseAsync(context);
            return;
        }

        // Sessions is resolved here, so a request outside the climber API builds no session services.
        var token = SessionCookie.Read(context.Request);
        if (token is null
            || await context.RequestServices.GetRequiredService<Sessions>().ResumeAsync(token, context.RequestAborted) is not { } session)
        {
            if (!required)
            {
                await next(context);
                return;
            }

            if (token is not null)
            {
                SessionCookie.Delete(context.Response);
            }

            await RefuseAsync(context);
            return;
        }

        context.Features.Set(new ClimberSessionFeature(session));
        context.Features.Set(new AccountLanguageFeature(session.Language));

        // Written as the response starts, so it survives an error response clearing the headers, unless the
        // endpoint has set the cookie itself (login, logout) or answered that the climber is not logged in.
        context.Response.OnStarting(() =>
        {
            if (!SessionCookie.IsSet(context.Response) && context.Response.StatusCode != StatusCodes.Status401Unauthorized)
            {
                SessionCookie.Write(context.Response, token, session.ExpiresAt);
            }

            return Task.CompletedTask;
        });
        await next(context);
    }

    private static Task RefuseAsync(HttpContext context) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status401Unauthorized, ClimberSessions.RequiredCode);
}

public static class ClimberPipeline
{
    // Design §2 'Request pipeline': session, then the account-state gate, then the legal gate, then the endpoint.
    // Runs after routing, since each step reads the endpoint's metadata.
    public static IApplicationBuilder UseClimberPipeline(this IApplicationBuilder app) =>
        app.UseMiddleware<SessionMiddleware>()
            .UseMiddleware<AccountStateGate>()
            .UseMiddleware<LegalGate>();
}

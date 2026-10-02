using ClimbOn.Api.Resources;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace ClimbOn.Api.Errors;

// Turns every error leaving the api into { code, message } (C45): domain errors, framework errors,
// unhandled exceptions, and any 4xx/5xx status that was set without a body.
public sealed class ErrorResponseMiddleware(RequestDelegate next, ILogger<ErrorResponseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, code) = Classify(exception);
            if (status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "Request failed with {Code}", code);
            }

            context.Response.Clear();
            await WriteAsync(context, status, code);
            return;
        }

        var response = context.Response;
        if (response.StatusCode >= StatusCodes.Status400BadRequest && !response.HasStarted
            && response.ContentLength is null && string.IsNullOrEmpty(response.ContentType))
        {
            await WriteAsync(context, response.StatusCode, ErrorCodes.ForStatus(response.StatusCode));
        }
    }

    private static (int Status, string Code) Classify(Exception exception) => exception switch
    {
        DomainError error => (StatusFor(error.Kind), error.Code),
        BadHttpRequestException badRequest => (badRequest.StatusCode, ErrorCodes.ForStatus(badRequest.StatusCode)),
        _ when DatabaseFailure.IsUnavailable(exception) =>
            (StatusCodes.Status503ServiceUnavailable, ErrorCodes.DatabaseUnavailable),
        _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError),
    };

    private static int StatusFor(DomainErrorKind kind) => kind switch
    {
        DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
        DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
        DomainErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };

    private static Task WriteAsync(HttpContext context, int status, string code)
    {
        var language = MessageLanguage.For(context);
        context.Response.StatusCode = status;
        context.Response.Headers.ContentLanguage = language.Tag();
        return context.Response.WriteAsJsonAsync(new ErrorBody(code, Texts.Get(code, language)), context.RequestAborted);
    }
}

public static class ErrorResponses
{
    // A startup filter rather than a Use call in Program, so the middleware wraps everything in the
    // pipeline, including what other startup filters add in front of Program's own middleware.
    public static IServiceCollection AddErrorResponses(this IServiceCollection services) =>
        services.AddSingleton<IStartupFilter, StartupFilter>();

    private sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMiddleware<ErrorResponseMiddleware>();
            next(app);
        };
    }
}

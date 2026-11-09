using Microsoft.Net.Http.Headers;

namespace ClimbOn.Api.Middleware;

// API responses carry account data (/me, chat and the like), so the browser must not keep them in its cache, where
// they would outlive logout on a shared device. Set as the response starts, so error responses carry it too.
public static class ApiNoStore
{
    private const string ApiPrefix = "/api";

    public static IApplicationBuilder UseApiNoStore(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(ApiPrefix))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = CacheControlHeaderValue.NoStoreString;
                    return Task.CompletedTask;
                });
            }

            return next(context);
        });
}

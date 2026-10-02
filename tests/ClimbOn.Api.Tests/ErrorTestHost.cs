using System.Net.Http.Json;
using ClimbOn.Api.Errors;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClimbOn.Api.Tests;

// The real api host plus endpoints that fail in known ways, registered from the test side only.
public sealed class ErrorTestHost(string environment) : WebApplicationFactory<Program>
{
    public const string Prefix = "/_test";

    // Stands in for the session middleware (task 11): marks the request as an account with this stored language.
    public const string AccountLanguageHeader = "X-Test-Account-Language";

    public static async Task<(HttpResponseMessage Response, ErrorBody? Body)> SendAsync(
        HttpClient client, HttpRequestMessage request)
    {
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = response.Content.Headers.ContentType?.MediaType == "application/json"
            ? await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken)
            : null;
        return (response, body);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, Endpoints>());
    }

    private sealed class Endpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(AccountLanguageHeader, out var tag))
                {
                    context.Features.Set(new AccountLanguageFeature(Languages.FromClientSetting(tag)));
                }

                return nextMiddleware(context);
            });
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet($"{Prefix}/domain-error/{{kind}}", (DomainErrorKind kind) =>
                {
                    throw new DomainError("test_rule_refused", kind);
                });
                endpoints.MapGet($"{Prefix}/bind", (int number) => Results.Ok(number));
                endpoints.MapPost($"{Prefix}/json", (Payload payload) => Results.Ok(payload));
                endpoints.MapGet($"{Prefix}/get-only", () => Results.Ok());
                endpoints.MapGet($"{Prefix}/throw", () =>
                {
                    throw new InvalidOperationException("unhandled");
                });
                endpoints.MapGet($"{Prefix}/status/{{status:int}}", (int status) => Results.StatusCode(status));
            });
            next(app);
        };
    }

    private sealed record Payload(string Name);
}

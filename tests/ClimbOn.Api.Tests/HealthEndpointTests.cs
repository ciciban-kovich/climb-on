using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ClimbOn.Api.Tests;

public sealed class HealthEndpointTests(HealthEndpointTests.HealthyDatabaseFactory factory)
    : IClassFixture<HealthEndpointTests.HealthyDatabaseFactory>
{
    [Fact]
    public async Task Health_returns_ok_status()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestContext.Current.CancellationToken);
        Assert.Equal("ok", body?.Status);
    }

    private sealed record HealthBody(string Status);

    // Replaces the database check with a healthy stub so this project needs no Docker;
    // the real check is covered in ClimbOn.Integration.Tests.
    public sealed class HealthyDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.Configure<HealthCheckServiceOptions>(options =>
            {
                var database = options.Registrations.Single(registration => registration.Name == DatabaseHealthCheck.Name);
                options.Registrations.Remove(database);
                options.Registrations.Add(new HealthCheckRegistration(
                    DatabaseHealthCheck.Name,
                    new HealthyStub(),
                    failureStatus: null,
                    tags: null));
            }));
    }

    private sealed class HealthyStub : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                [DatabaseHealthCheck.StateKey] = "ok",
            }));
    }
}

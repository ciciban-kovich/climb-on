using System.Net;
using System.Net.Http.Json;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests;

// The smoke check is `curl -fsS /health`: only the status code decides it, so every case asserts the code.
public sealed class HealthEndpointTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Healthy_returns_200_ok()
    {
        var (status, body) = await GetHealthAsync(Factory);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new HealthBody("ok", "ok"), body);
    }

    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Pending_migration_returns_503()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await using var unmigrated = ApiFactory.For(new TestDatabaseSettings(server.ConnectionString, SkipMigrations: true));
        await unmigrated.InitializeAsync();

        var (status, body) = await GetHealthAsync(unmigrated);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("migration_pending", body?.Database);
    }

    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Unreachable_database_returns_503()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await using var stopped = ApiFactory.For(new TestDatabaseSettings(server.ConnectionString, SkipMigrations: false));
        await stopped.InitializeAsync();
        Assert.Equal(HttpStatusCode.OK, (await GetHealthAsync(stopped)).Status);

        await server.StopAsync(ct);
        var (status, body) = await GetHealthAsync(stopped);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("unreachable", body?.Database);
    }

    private static async Task<(HttpStatusCode Status, HealthBody? Body)> GetHealthAsync(ApiFactory factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestContext.Current.CancellationToken);
        return (response.StatusCode, body);
    }

    private sealed record HealthBody(string Status, string Database);
}

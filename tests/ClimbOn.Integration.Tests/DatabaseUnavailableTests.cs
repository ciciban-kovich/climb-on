using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Errors;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests;

public sealed class DatabaseUnavailableTests
{
    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Ordinary_endpoint_returns_503_with_code()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await using var stopped = ApiFactory.For(new TestDatabaseSettings(server.ConnectionString, SkipMigrations: false));
        await stopped.InitializeAsync();
        using var client = stopped.CreateClient();
        using (var reachable = await client.GetAsync($"{TestEndpoints.Prefix}/database", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, reachable.StatusCode);
        }

        await server.StopAsync(ct);
        using var response = await client.GetAsync($"{TestEndpoints.Prefix}/database", ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(ct);
        Assert.Equal(ErrorCodes.DatabaseUnavailable, body?.Code);
    }
}

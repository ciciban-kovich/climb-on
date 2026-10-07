using System.Text.Json.Nodes;
using ClimbOn.Api.OpenApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ClimbOn.Api.Tests;

// The committed document is what the running api describes (C47); tools/check-api-fresh.sh
// checks that it and the generated client are what the build produces (C48).
public sealed class OpenApiContractTests(OpenApiContractTests.DevelopmentFactory factory)
    : IClassFixture<OpenApiContractTests.DevelopmentFactory>
{
    [Fact]
    public async Task Served_climber_document_equals_the_committed_file()
    {
        using var client = factory.CreateClient();

        var served = await client.GetStringAsync($"/openapi/{ApiDocuments.Climber}.json", TestContext.Current.CancellationToken);
        var committed = await File.ReadAllTextAsync(
            Path.Combine(Repo.Root, "openapi", $"{ApiDocuments.Climber}.json"), TestContext.Current.CancellationToken);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(committed), JsonNode.Parse(served)),
            $"openapi/{ApiDocuments.Climber}.json is stale; run tools/regenerate-api.sh. Served:\n{served}");
    }

    // The document is served in Development only; it needs no database.
    public sealed class DevelopmentFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }
}

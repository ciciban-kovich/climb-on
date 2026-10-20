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

    // C45: the generated client reads the error code only for statuses the document declares.
    [Fact]
    public async Task Every_operation_declares_the_error_body_for_4xx_and_5xx()
    {
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(
            await client.GetStringAsync($"/openapi/{ApiDocuments.Climber}.json", TestContext.Current.CancellationToken))!;
        var operations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => (Name: $"{operation.Key} {path.Key}", Operation: operation.Value!)))
            .ToList();

        Assert.NotEmpty(operations);
        Assert.Empty(
            from operation in operations
            from range in new[] { "4XX", "5XX" }
            let reference = operation.Operation["responses"]?[range]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>()
            where reference != "#/components/schemas/ErrorBody"
            select $"{operation.Name} {range}");
    }

    // The document is served in Development only; it needs no database.
    public sealed class DevelopmentFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }
}

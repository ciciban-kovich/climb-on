using System.Net;
using ClimbOn.Api.Errors;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ClimbOn.Api.Tests;

// Design §2 'Same-origin climber client': the api serves the climber PWA, and client routes load its index.html.
public sealed class ClientHostingTests : IClassFixture<ClientHostingTests.Host>
{
    private const string ShellMarker = "<div id=\"app\">";

    private readonly HttpClient client;

    public ClientHostingTests(Host host) => client = host.CreateClient();

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/confirm")]
    [InlineData("/some/deep/client/route")]
    public async Task Client_route_falls_back_to_index(string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(ShellMarker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/api/climber/no-such-route")]
    [InlineData("/api/no-such-api")]
    [InlineData("/api")]
    [InlineData("/API/climber/no-such-route")]
    public async Task Unknown_api_route_is_json_404(string path)
    {
        var (response, body) = await ErrorTestHost.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, path));

        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ErrorCodes.NotFound, body?.Code);
        }
    }

    // The fallback must not stand in for an API route that exists under another method.
    [Fact]
    public async Task Known_api_route_keeps_its_405()
    {
        var (response, body) = await ErrorTestHost.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, "/api/climber/login"));

        using (response)
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Equal(ErrorCodes.MethodNotAllowed, body?.Code);
        }
    }

    [Theory]
    [InlineData("/no-such-file.js")]
    [InlineData("/_framework/no-such-file.js")]
    [InlineData("/some/deep/no-such-file.js")]
    public async Task Missing_file_is_404_not_the_shell(string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/_framework/blazor.webassembly.js")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/service-worker.js")]
    public async Task Client_files_are_served(string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // No database: nothing here reads one, and a call that did would fail at once.
    // Run from build output, MapStaticAssets adds a GET fallback for files added after startup, which a published
    // app does not have; it would answer every GET this class asserts about. Turned off, routing is as published.
    public sealed class Host : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder
                .UseSetting($"ConnectionStrings:{ClimbOnDbContext.ConnectionStringName}",
                    "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=5")
                .UseSetting("DisableStaticAssetNotFoundRuntimeFallback", "true");
    }
}

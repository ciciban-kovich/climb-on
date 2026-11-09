using System.Net;
using Xunit;

namespace ClimbOn.Api.Tests;

// No api response is kept in the browser's cache, errors included, while the client's shell stays cacheable for the
// PWA. Account data itself is checked against a database in the integration tests (LoginTests).
public sealed class ApiCachingTests(ClientHostingTests.Host host) : IClassFixture<ClientHostingTests.Host>
{
    private readonly HttpClient client = host.CreateClient();

    [Theory]
    [InlineData("/api/climber/me", HttpStatusCode.Unauthorized)]
    [InlineData("/api/no-such-api", HttpStatusCode.NotFound)]
    [InlineData("/API/climber/no-such-route", HttpStatusCode.NotFound)]
    public async Task Api_responses_are_not_stored(string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // A body that does not parse fails inside the endpoint, so the error response is written from scratch.
    [Fact]
    public async Task Api_error_written_from_an_exception_is_not_stored()
    {
        using var body = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/climber/login", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    public async Task Client_shell_is_not_marked_no_store(string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(true, response.Headers.CacheControl?.NoStore);
    }
}

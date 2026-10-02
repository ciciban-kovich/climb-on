using System.Net;
using System.Text;
using ClimbOn.Api.Errors;
using Xunit;

namespace ClimbOn.Api.Tests;

// Errors the framework produces, in both environments: Development throws where Production sets a bare status.
public sealed class FrameworkErrorTests(FrameworkErrorTests.Hosts hosts) : IClassFixture<FrameworkErrorTests.Hosts>
{
    public static TheoryData<string> Environments => ["Production", "Development"];

    public static TheoryData<int> ErrorStatuses => [.. Enumerable.Range(400, 200)];

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Model_binding_failure_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Get, "/bind?number=not-a-number");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body?.Code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unknown_route_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Get, "/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, body?.Code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Method_not_allowed_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Delete, "/get-only");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(ErrorCodes.MethodNotAllowed, body?.Code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unsupported_media_type_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Post, "/json",
            new StringContent("name=x", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(ErrorCodes.UnsupportedMediaType, body?.Code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Malformed_json_body_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Post, "/json",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body?.Code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unhandled_exception_has_code(string environment)
    {
        var (response, body) = await SendAsync(environment, HttpMethod.Get, "/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ErrorCodes.InternalError, body?.Code);
    }

    [Theory]
    [MemberData(nameof(ErrorStatuses))]
    public async Task Any_bodyless_error_status_gets_code(int status)
    {
        var (response, body) = await SendAsync("Production", HttpMethod.Get, $"/status/{status}");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(ErrorCodes.ForStatus(status), body?.Code);
        Assert.False(string.IsNullOrWhiteSpace(body?.Message));
    }

    private async Task<(HttpResponseMessage Response, ErrorBody? Body)> SendAsync(
        string environment, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, ErrorTestHost.Prefix + path) { Content = content };
        return await ErrorTestHost.SendAsync(hosts.Client(environment), request);
    }

    public sealed class Hosts : IDisposable
    {
        private readonly Dictionary<string, (ErrorTestHost Factory, HttpClient Client)> hosts = [];

        public HttpClient Client(string environment)
        {
            lock (hosts)
            {
                if (!hosts.TryGetValue(environment, out var host))
                {
                    var factory = new ErrorTestHost(environment);
                    host = (factory, factory.CreateClient());
                    hosts[environment] = host;
                }

                return host.Client;
            }
        }

        public void Dispose()
        {
            foreach (var (factory, client) in hosts.Values)
            {
                client.Dispose();
                factory.Dispose();
            }
        }
    }
}

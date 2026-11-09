using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClimbOn.Client.Api;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace ClimbOn.Client.Tests;

// Stands in for the api behind the generated client, so pages are tested through the code they really call.
// A route with no answer set gets the api's 404 body.
public sealed class FakeApi : HttpMessageHandler
{
    public const string BaseUrl = "http://localhost";

    private readonly Dictionary<(HttpMethod Method, string Path), Func<HttpResponseMessage>> answers = [];

    private readonly List<RecordedRequest> requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    public FakeApi Answer(HttpMethod method, string path, HttpStatusCode status, object? body = null)
    {
        answers[(method, path)] = () => new HttpResponseMessage(status)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        return this;
    }

    // The api's error body (C45).
    public FakeApi Fail(HttpMethod method, string path, HttpStatusCode status, string code, string message) =>
        Answer(method, path, status, new { code, message });

    public RecordedRequest Single(HttpMethod method, string path) =>
        Assert.Single(Requests, request => request.Method == method && request.Path == path);

    public ClimberApiClient Client() =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: new HttpClient(this))
        {
            BaseUrl = BaseUrl,
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (requests)
        {
            requests.Add(new RecordedRequest(request.Method, uri.AbsolutePath, uri.Query, uri.Fragment, body));
        }

        return answers.TryGetValue((request.Method, uri.AbsolutePath), out var answer)
            ? answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { code = "not_found", message = "Not found." }) };
    }
}

public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Fragment, string? Body)
{
    public JsonElement Json => JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request had no body.")).RootElement;

    public string? StringProperty(string name) =>
        Json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    public int? IntProperty(string name) =>
        Json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;
}

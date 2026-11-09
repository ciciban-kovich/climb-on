using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(5);

    private readonly Dictionary<(HttpMethod Method, string Path), Func<Task<HttpResponseMessage>>> answers = [];

    private readonly ConcurrentDictionary<(HttpMethod Method, string Path), TaskCompletionSource> arrivals = [];

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
        answers[(method, path)] = () => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = body is null ? null : JsonContent.Create(body),
        });
        return this;
    }

    // A reply that is not the api's: a proxy's error page, or a body that does not parse.
    public FakeApi AnswerRaw(HttpMethod method, string path, HttpStatusCode status, string mediaType, string body)
    {
        answers[(method, path)] = () => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
        return this;
    }

    // No reply at all: the network fails, as offline or with the api down.
    public FakeApi Unreachable(HttpMethod method, string path)
    {
        answers[(method, path)] = () => throw new HttpRequestException("The api cannot be reached.");
        return this;
    }

    // The answer already set for the route waits until another request has arrived; past HoldLimit it fails.
    public FakeApi HoldUntilRequested(HttpMethod method, string path, HttpMethod otherMethod, string otherPath)
    {
        var answer = answers[(method, path)];
        var other = Arrival(otherMethod, otherPath).Task;
        answers[(method, path)] = async () =>
        {
            await other.WaitAsync(HoldLimit);
            return await answer();
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

        Arrival(request.Method, uri.AbsolutePath).TrySetResult();
        return answers.TryGetValue((request.Method, uri.AbsolutePath), out var answer)
            ? await answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { code = "not_found", message = "Not found." }) };
    }

    private TaskCompletionSource Arrival(HttpMethod method, string path) =>
        arrivals.GetOrAdd((method, path), _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Fragment, string? Body)
{
    public JsonElement Json => JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request had no body.")).RootElement;

    public string? StringProperty(string name) =>
        Json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    public int? IntProperty(string name) =>
        Json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;
}

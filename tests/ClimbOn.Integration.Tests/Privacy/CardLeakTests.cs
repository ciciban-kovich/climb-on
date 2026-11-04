using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClimbOn.Api.Climber;
using ClimbOn.Integration.Tests.Harness;
using ClimbOn.Integration.Tests.Invites;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClimbOn.Integration.Tests.Privacy;

// S50, S53 over every climber GET in the route table: called as A, while B is connected to A, C has invited A, D is
// invited by A and E is in A's suggestion list, no response carries any of their emails, dates of birth, postcodes or
// accepted partner sexes. A's own /me and profile return A's own values, which differ from each of theirs.
public sealed class CardLeakTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string ClimberPrefix = "/api/climber";

    // A accepts every sex; each other climber accepts a set that differs from A's.
    private static readonly string[] AllSexes = ["female", "male", "other"];

    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("SpecPartial", "S50")]
    [Trait("SpecPartial", "S53")]
    public async Task No_endpoint_returns_email_dob_postcode_or_accepted_sexes()
    {
        var a = await climbers.ClimberAsync(profile => profile with { AcceptedSexes = AllSexes });
        var others = new List<(InviteClimber Climber, string Postcode, string[] Accepted)>();
        foreach (var (postcode, accepted) in new[]
        {
            ("0250", new[] { "female" }), ("0355", new[] { "female", "other" }),
            ("0560", new[] { "female", "male" }), ("1300", new[] { "female" }),
        })
        {
            var climber = await climbers.ClimberAsync(profile => profile with
            {
                Postcode = Factory.Data.Postcode(postcode),
                AcceptedSexes = accepted,
            });
            others.Add((climber, postcode, accepted));
        }

        var (b, c, d, e) = (others[0].Climber, others[1].Climber, others[2].Climber, others[3].Climber);
        var connection = await ConnectAsync(a, b);
        await climbers.InvitedAsync(c, a);
        await climbers.InvitedAsync(a, d);
        await SendMessageAsync(b, connection);

        var bodies = new Dictionary<string, JsonElement>();
        foreach (var route in ClimberGets())
        {
            bodies[route] = await GetAsync(a, Path(route, connection));
        }

        // Each other climber is really shown somewhere, so the scan below is not vacuous.
        Assert.Equal([e.Name], Strings(bodies[ClimberPrefix + "/suggestions"], "displayName"));
        Assert.Contains(c.Name, Strings(bodies[ClimberPrefix + "/invites"], "displayName"));
        Assert.Contains(d.Name, Strings(bodies[ClimberPrefix + "/invites"], "displayName"));
        Assert.Contains(b.Name, Strings(bodies[ClimberPrefix + "/connections"], "displayName"));

        var leaks = new List<string>();
        foreach (var (route, body) in bodies)
        {
            var values = Strings(body).ToList();
            foreach (var (climber, postcode, accepted) in others)
            {
                var birth = climber.Account.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                leaks.AddRange(values.Where(value => value.Contains(climber.Account.Email, StringComparison.OrdinalIgnoreCase))
                    .Select(_ => $"{route}: email of {climber.Name}"));
                leaks.AddRange(values.Where(value => value.Contains(birth, StringComparison.Ordinal))
                    .Select(_ => $"{route}: date of birth of {climber.Name}"));
                leaks.AddRange(values.Where(value => !Guid.TryParse(value, out var _)
                        && Regex.IsMatch(value, $"(?<![0-9]){postcode}(?![0-9])", RegexOptions.CultureInvariant))
                    .Select(_ => $"{route}: postcode of {climber.Name}"));
                Assert.NotEqual(AllSexes, accepted);
            }

            leaks.AddRange(AcceptedSexes(body).Where(set => !set.SequenceEqual(AllSexes))
                .Select(set => $"{route}: accepted sexes [{string.Join(", ", set)}]"));
        }

        Assert.Empty(leaks);
    }

    // Every route that returns a card type, in any method, is a climber GET, so the test above calls it; and the
    // route table does show the card types where they are known to be.
    [Fact]
    public void Every_card_endpoint_is_covered()
    {
        var carding = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Any(produced => Carries(produced.Type)))
            .Select(endpoint => (Route: endpoint.RoutePattern.RawText ?? string.Empty, Methods: Methods(endpoint)))
            .ToList();

        Assert.Superset(
            new HashSet<string> { ClimberPrefix + "/suggestions", ClimberPrefix + "/invites", ClimberPrefix + "/connections" },
            carding.Select(endpoint => endpoint.Route).ToHashSet());
        Assert.Empty(carding.Where(endpoint => !ClimberGets().Contains(endpoint.Route)).Select(endpoint => $"{string.Join(',', endpoint.Methods)} {endpoint.Route}"));
    }

    // Every GET route of the climber API.
    private List<string> ClimberGets() =>
        Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => Methods(endpoint).Contains("GET"))
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(route => route.StartsWith(ClimberPrefix + "/", StringComparison.Ordinal))
            .Distinct()
            .ToList();

    private static IReadOnlyList<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

    // The route with its parameters filled in; a parameter this test does not know fails it, so a new parameterised
    // GET is exercised with a real value or not at all.
    private static string Path(string route, Guid connection) =>
        Regex.Replace(route, "{([^}:]+)[^}]*}", match => match.Groups[1].Value switch
        {
            "id" when route.StartsWith(ClimberPrefix + "/connections/", StringComparison.Ordinal) =>
                connection.ToString("D"),
            var name => throw new InvalidOperationException($"No value for {{{name}}} in {route}."),
        });

    // Whether a response of this type can hold a card, anywhere in its graph.
    private static bool Carries(Type? type) => Carries(type, []);

    private static bool Carries(Type? type, HashSet<Type> seen)
    {
        if (type is null || type == typeof(string) || type.IsPrimitive || !seen.Add(type))
        {
            return false;
        }

        return type == typeof(ClimberCardResponse)
            || (type.IsArray && Carries(type.GetElementType(), seen))
            || type.GetGenericArguments().Any(argument => Carries(argument, seen))
            || type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => Carries(property.PropertyType, seen));
    }

    private static IEnumerable<string> Strings(JsonElement element, string? name = null) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property =>
            name is not null && property.Name == name && property.Value.ValueKind == JsonValueKind.String
                ? [property.Value.GetString()!]
                : Strings(property.Value, name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(item => Strings(item, name)),
        JsonValueKind.String when name is null => [element.GetString()!],
        _ => [],
    };

    // Every acceptedSexes array in the body, sorted.
    private static IEnumerable<string[]> AcceptedSexes(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property =>
            string.Equals(property.Name, "acceptedSexes", StringComparison.OrdinalIgnoreCase)
                ? [property.Value.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray()]
                : AcceptedSexes(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AcceptedSexes),
        _ => [],
    };

    private async Task<Guid> ConnectAsync(InviteClimber from, InviteClimber to)
    {
        var invite = await climbers.InvitedAsync(from, to);
        using (var response = await climbers.AcceptAsync(to, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var list = await GetAsync(from, ClimberPrefix + "/connections");
        return list.GetProperty("connections").EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private async Task SendMessageAsync(InviteClimber from, Guid connection)
    {
        using var response = await climbers.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"{ClimberPrefix}/connections/{connection}/messages")
            {
                Content = JsonContent.Create(new MessageRequest(Factory.Data.Message())),
            },
            from.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> GetAsync(InviteClimber climber, string path)
    {
        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, path), climber.Token);
        Assert.True(response.IsSuccessStatusCode, $"GET {path}: {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }
}

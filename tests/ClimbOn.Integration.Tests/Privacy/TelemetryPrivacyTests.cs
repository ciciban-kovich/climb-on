using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using Xunit;

namespace ClimbOn.Integration.Tests.Privacy;

// C32, C33. These tests drive the per-test lease themselves, so that a leak they cause on purpose
// can be taken out of the capture, or observed failing an ordinary test's lifecycle.
public sealed class TelemetryPrivacyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Log_leak_is_detected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (await factory.BeginTestAsync(ct))
        {
            var account = factory.Data.Account();
            await PostProbeAsync("log", account.Email, ct);

            var leaks = await factory.Telemetry.TakeLeaksAsync(factory.Fixtures);

            Assert.Contains(new TelemetryLeak("email", "log", typeof(TestEndpoints).FullName!, "message"), leaks);
        }
    }

    [Fact]
    public async Task Span_tag_leak_is_detected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (await factory.BeginTestAsync(ct))
        {
            var account = factory.Data.Account();
            await PostProbeAsync("tag", account.DisplayName, ct);

            var leaks = await factory.Telemetry.TakeLeaksAsync(factory.Fixtures);

            Assert.Contains(leaks, leak => leak is { Kind: "display-name", Signal: "span", Field: "tag:probe.value" });
        }
    }

    [Fact]
    public async Task Query_string_is_not_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (await factory.BeginTestAsync(ct))
        {
            var account = factory.Data.Account();
            using var client = factory.CreateClient();

            // Unescaped, so a recorded path or query would hold the fixture value verbatim.
            using var response = await client.GetAsync(
                $"{TestEndpoints.Prefix}/telemetry/query/{account.Email}?email={account.Email}", ct);
            var captured = await factory.Telemetry.TakeAsync();

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            const string route = TestEndpoints.Prefix + "/telemetry/query/{value}";
            Assert.Contains(captured, item => item.Signal == "span"
                && Equals(item.Field("tag:http.route"), route)
                && Equals(item.Field("displayName"), "GET " + route));
            Assert.Empty(TelemetryCapture.FindLeaks(captured, factory.Fixtures));
        }
    }

    [Fact]
    public async Task Scan_runs_for_every_test_via_factory()
    {
        var ct = TestContext.Current.CancellationToken;
        var ordinary = new OrdinaryTest(factory);

        // The lifecycle xunit gives every IntegrationTest; nothing here calls the scan.
        await ordinary.InitializeAsync();
        Exception? failure;
        try
        {
            await PostProbeAsync("log", factory.Data.Account().Email, ct);
        }
        finally
        {
            failure = await Record.ExceptionAsync(() => ordinary.DisposeAsync().AsTask());
        }

        var leak = Assert.IsType<TelemetryLeakException>(failure);
        Assert.Contains(leak.Leaks, found => found is { Kind: "email", Signal: "log" });
        Assert.DoesNotContain(factory.Fixtures.All, value => leak.Message.Contains(value, StringComparison.OrdinalIgnoreCase));

        // The failed scan consumed the leak and released the database: the next test starts clean.
        await using (await factory.BeginTestAsync(ct))
        {
        }
    }

    [Fact]
    public void Postcode_matches_as_a_whole_token_in_text_only()
    {
        var fixtures = new FixtureValues();
        fixtures.Register(FixtureValues.PostcodeKind, "0150");
        TelemetryItem[] captured =
        [
            new("log", "address", [new("message", "Postnummer 0150 Oslo")]),
            new("log", "sentence", [new("message", "Flyttet til 0150.")]),
            new("log", "numbers", [new("message", "Took 101501 ms on port 30150, then 1.0150 s and 0150.5 ms")]),
            new("span", "numeric", [new("tag:server.port", 150), new("tag:duration", 0150L)]),
        ];

        var leaks = TelemetryCapture.FindLeaks(captured, fixtures);

        Assert.Equal(
            [
                new TelemetryLeak(FixtureValues.PostcodeKind, "log", "address", "message"),
                new TelemetryLeak(FixtureValues.PostcodeKind, "log", "sentence", "message"),
            ],
            leaks);
    }

    // A comma separates tokens; only a dot next to a digit makes the value part of a decimal.
    [Theory]
    [InlineData("0150,0151", true)]
    [InlineData("[0150,0151]", true)]
    [InlineData("Postnumre 0151,0150", true)]
    [InlineData("Storgata 1,0150 Oslo", true)]
    [InlineData("0150 Oslo", true)]
    [InlineData("1.0150 ms", false)]
    [InlineData("0150.5", false)]
    public void Postcode_next_to_a_comma_matches_and_inside_a_decimal_does_not(string message, bool leaks)
    {
        var fixtures = new FixtureValues();
        fixtures.Register(FixtureValues.PostcodeKind, "0150");
        TelemetryItem[] captured = [new("log", "probe", [new("message", message)])];

        var found = TelemetryCapture.FindLeaks(captured, fixtures);

        Assert.Equal(leaks, found.Contains(new TelemetryLeak(FixtureValues.PostcodeKind, "log", "probe", "message")));
    }

    [Fact]
    public async Task Date_of_birth_logged_as_a_date_is_detected()
    {
        var fixtures = new FixtureValues();
        var account = new TestData(fixtures).Account();
        var capture = new TelemetryCapture();
        using (var loggers = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(capture.LogProcessor);
        })))
        {
            loggers.CreateLogger("probe").LogInformation("Born {DateOfBirth}", account.DateOfBirth);
        }

        var leaks = await capture.TakeLeaksAsync(fixtures);

        Assert.Contains(new TelemetryLeak("date-of-birth", "log", "probe", "message"), leaks);
        Assert.Contains(new TelemetryLeak("date-of-birth", "log", "probe", "attribute:DateOfBirth"), leaks);
    }

    [Fact]
    public async Task Metric_leak_is_reported_once()
    {
        var fixtures = new FixtureValues();
        var account = new TestData(fixtures).Account();
        var capture = new TelemetryCapture();
        using var meter = new Meter("ClimbOn.Probe");
        using var meters = Sdk.CreateMeterProviderBuilder().AddMeter(meter.Name).AddReader(capture.MetricReader).Build();

        meter.CreateCounter<long>("probe_total").Add(1, new KeyValuePair<string, object?>("who", account.Email));
        var first = await capture.TakeLeaksAsync(fixtures);
        var second = await capture.TakeLeaksAsync(fixtures);

        Assert.Contains(new TelemetryLeak("email", "metric", "probe_total", "tag:who"), first);
        Assert.Empty(second);
    }

    private async Task PostProbeAsync(string probe, string value, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"{TestEndpoints.Prefix}/telemetry/{probe}", new ProbeValue(value), ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class OrdinaryTest(ApiFactory factory) : IntegrationTest(factory);
}

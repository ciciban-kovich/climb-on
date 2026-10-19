using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Jobs;
using ClimbOn.Domain.Postcodes;
using ClimbOn.Infrastructure.Postcodes;
using ClimbOn.Integration.Tests.Harness;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Postcodes;

// Each test starts from an empty postcode table and puts the fixture table back when it ends.
public sealed class PostcodeImportTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string CompletedEvent = "PostcodeImportCompleted";

    // In the excerpt, missing from the next version.
    private const string Dropped = "9008";

    // In the excerpt, moved in the next version.
    private const string Moved = "4006";

    // Only in the next version.
    private const string Added = "8006";

    // In the next version without an area.
    private const string WithoutArea = "0101";

    [Fact]
    [Trait("SpecPartial", "S101")]
    [Trait("SpecPartial", "S101.3")]
    public async Task First_import_adds_every_postcode_area()
    {
        await using var table = await ClearAsync();

        await ImportAsync(KartverketFixture.Excerpt);

        Assert.Equal(await ExpectedAsync(KartverketFixture.Excerpt), await TableAsync());
        Assert.Equal(new PostcodeImportResult(8, 0, 0, 0, 8), await CompletedAsync());
    }

    [Fact]
    [Trait("Spec", "S100")]
    public async Task Postcode_without_an_area_is_not_imported()
    {
        await using var table = await ClearAsync();

        await ImportAsync(KartverketFixture.NextVersion);

        var rows = await TableAsync();
        Assert.DoesNotContain(rows, row => row.Code == WithoutArea);
        Assert.Equal(await ExpectedAsync(KartverketFixture.NextVersion), rows);
    }

    [Fact]
    [Trait("Spec", "S101.1")]
    public async Task Rerun_of_the_same_version_changes_nothing()
    {
        await using var table = await ClearAsync();
        await ImportAsync(KartverketFixture.Excerpt);
        var rows = await TableAsync();
        var versions = await RowVersionsAsync();
        await CompletedAsync();

        await ImportAsync(KartverketFixture.Excerpt);

        Assert.Equal(rows, await TableAsync());
        Assert.Equal(versions, await RowVersionsAsync());
        Assert.Equal(new PostcodeImportResult(0, 0, 0, 0, 8), await CompletedAsync());
    }

    [Fact]
    [Trait("SpecPartial", "S102")]
    [Trait("Spec", "S102.1")]
    [Trait("SpecPartial", "S101.3")]
    public async Task New_version_adds_changes_and_retires_keeping_the_last_point()
    {
        await using var table = await ClearAsync();
        await ImportAsync(KartverketFixture.Excerpt);
        var before = (await TableAsync()).ToDictionary(row => row.Code);
        await CompletedAsync();

        await ImportAsync(KartverketFixture.NextVersion);

        var after = (await TableAsync()).ToDictionary(row => row.Code);
        var next = (await ExpectedAsync(KartverketFixture.NextVersion)).ToDictionary(row => row.Code);
        Assert.Equal(before[Dropped] with { Retired = true }, after[Dropped]);
        Assert.Equal(next[Moved], after[Moved]);
        Assert.NotEqual(before[Moved], after[Moved]);
        Assert.Equal(next[Added], after[Added]);
        Assert.Equal(before.Keys.Append(Added).Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new PostcodeImportResult(1, 1, 1, 0, 8), await CompletedAsync());
    }

    [Fact]
    [Trait("SpecPartial", "S102.2")]
    [Trait("SpecPartial", "S102.3")]
    public async Task Retired_postcode_cannot_be_chosen_and_keeps_its_point()
    {
        await using var table = await ClearAsync();
        await ImportAsync(KartverketFixture.Excerpt);
        var last = (await TableAsync()).Single(row => row.Code == Dropped);
        await ImportAsync(KartverketFixture.NextVersion);
        var client = Factory.CreateClient();
        var code = Factory.Data.Postcode(Dropped);
        var ct = TestContext.Current.CancellationToken;

        using var validation = await client.PostAsJsonAsync($"{TestEndpoints.Prefix}/postcodes/validate", new ProbeValue(code), ct);
        using var point = await client.PostAsJsonAsync($"{TestEndpoints.Prefix}/postcodes/point", new ProbeValue(code), ct);

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Equal(PostcodeRules.RetiredCode, (await validation.Content.ReadFromJsonAsync<ErrorBody>(ct))?.Code);
        Assert.Equal(new PointProbe(last.Latitude, last.Longitude), await point.Content.ReadFromJsonAsync<PointProbe>(ct));
    }

    [Fact]
    [Trait("Spec", "S102.4")]
    public async Task Retired_postcode_that_reappears_is_reactivated_with_the_new_point()
    {
        await using var table = await ClearAsync();
        var stale = new FixturePostcode(Dropped, 69.0, 18.0, Retired: true);
        await InsertAsync(stale);

        await ImportAsync(KartverketFixture.Excerpt);

        var reactivated = (await TableAsync()).Single(row => row.Code == Dropped);
        Assert.Equal((await ExpectedAsync(KartverketFixture.Excerpt)).Single(row => row.Code == Dropped), reactivated);
        Assert.NotEqual((stale.Latitude, stale.Longitude), (reactivated.Latitude, reactivated.Longitude));
        Assert.Equal(new PostcodeImportResult(7, 0, 0, 1, 8), await CompletedAsync());
    }

    // The next version writes three rows; the second write fails, after the first has been applied.
    [Fact]
    [Trait("Spec", "S101.2")]
    public async Task Failure_mid_apply_leaves_table_unchanged()
    {
        await using var table = await ClearAsync();
        await ImportAsync(KartverketFixture.Excerpt);
        var rows = await TableAsync();
        await CompletedAsync();

        await using (var trigger = await FailSecondWriteAsync())
        {
            Factory.Kartverket.Serve(KartverketFixture.NextVersion);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => Factory.RunJobAsync(PostcodesJob.JobName));
            Assert.Contains("injected import failure", failure.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, await trigger.WritesAsync());
        }

        Assert.Equal(rows, await TableAsync());
        Assert.Empty(await CompletedEventsAsync());
    }

    private Task<IAsyncDisposable> ClearAsync() =>
        FixturePostcodes.ClearForImportAsync(Factory.ConnectionString, TestContext.Current.CancellationToken);

    private async Task ImportAsync(string file)
    {
        Factory.Kartverket.Serve(file);
        Assert.Equal(0, await Factory.RunJobAsync(PostcodesJob.JobName));
    }

    private static async Task<List<FixturePostcode>> ExpectedAsync(string file)
    {
        await using var geojson = File.OpenRead(KartverketFixture.PathOf(file));
        var areas = await KartverketSource.ReadAreasAsync(geojson, TestContext.Current.CancellationToken);
        return areas
            .Select(area => new FixturePostcode(area.Code, area.ReferencePoint.Y, area.ReferencePoint.X, Retired: false))
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<FixturePostcode>> TableAsync() =>
        (await Factory.CreateClient().GetFromJsonAsync<List<FixturePostcode>>(
            $"{TestEndpoints.Prefix}/postcodes", TestContext.Current.CancellationToken))!;

    // The one completion event since the last call, as counts.
    private async Task<PostcodeImportResult> CompletedAsync()
    {
        var completed = Assert.Single(await CompletedEventsAsync());
        return new PostcodeImportResult(
            Count(completed, "Added"),
            Count(completed, "Changed"),
            Count(completed, "Retired"),
            Count(completed, "Reactivated"),
            Count(completed, "ActiveTotal"));
    }

    // Taking the capture removes it from the lease's C33 scan, so the scan runs here instead.
    private async Task<List<TelemetryItem>> CompletedEventsAsync()
    {
        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        return captured.Where(item => item.Signal == "log" && Equals(item.Field("eventName"), CompletedEvent)).ToList();
    }

    private static int Count(TelemetryItem item, string name) => Assert.IsType<int>(item.Field($"attribute:{name}"));

    // xmin changes whenever a row is rewritten, even to the same values.
    private async Task<List<(string Code, uint Version)>> RowVersionsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT code, xmin FROM postcodes ORDER BY code", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var versions = new List<(string, uint)>();
        while (await reader.ReadAsync(ct))
        {
            versions.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        }

        return versions;
    }

    private async Task InsertAsync(FixturePostcode row)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "INSERT INTO postcodes (code, lat, lon, retired) VALUES (@code, @lat, @lon, @retired)", connection);
        command.Parameters.AddWithValue("code", row.Code);
        command.Parameters.AddWithValue("lat", row.Latitude);
        command.Parameters.AddWithValue("lon", row.Longitude);
        command.Parameters.AddWithValue("retired", row.Retired);
        await command.ExecuteNonQueryAsync(ct);
    }

    // A row trigger that lets the first write through and fails the next; removed on dispose.
    private async Task<FailureTrigger> FailSecondWriteAsync()
    {
        await ExecuteAsync(
            Factory.ConnectionString,
            """
            CREATE SEQUENCE import_failure_writes;
            CREATE FUNCTION import_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF nextval('import_failure_writes') >= 2 THEN
                    RAISE EXCEPTION 'injected import failure';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER import_failure BEFORE INSERT OR UPDATE ON postcodes
                FOR EACH ROW EXECUTE FUNCTION import_failure();
            """,
            TestContext.Current.CancellationToken);
        return new FailureTrigger(Factory.ConnectionString);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class FailureTrigger(string connectionString) : IAsyncDisposable
    {
        // Rows the import tried to write while the trigger was in place.
        public async Task<long> WritesAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("SELECT last_value FROM import_failure_writes", connection);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public async ValueTask DisposeAsync() => await ExecuteAsync(
            connectionString,
            """
            DROP TRIGGER import_failure ON postcodes;
            DROP FUNCTION import_failure();
            DROP SEQUENCE import_failure_writes;
            """,
            CancellationToken.None);
    }
}

using System.Net;
using System.Net.Http.Json;
using ClimbOn.Integration.Tests.Harness;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Postcodes;

public sealed class PostcodeLookupTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    [Trait("Spec", "S16")]
    public async Task Active_postcode_resolves_to_its_stored_point()
    {
        var stored = FixturePostcodes.Get("5003");

        var point = await PointAsync(Factory.Data.Postcode(stored.Code));

        Assert.Equal(new PointProbe(stored.Latitude, stored.Longitude), point);
    }

    [Fact]
    [Trait("Spec", "S102.3")]
    public async Task Retired_postcode_keeps_its_point()
    {
        var stored = FixturePostcodes.Get(FixturePostcodes.Retired);
        Assert.True(stored.Retired);

        var point = await PointAsync(Factory.Data.Postcode(stored.Code));

        Assert.Equal(new PointProbe(stored.Latitude, stored.Longitude), point);
    }

    [Fact]
    [Trait("Spec", "S16")]
    public async Task Postcode_not_in_the_table_has_no_point()
    {
        using var response = await PostAsync(Factory.Data.Postcode(FixturePostcodes.Unknown));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // C44: the fixture table is seeded once per factory and kept by the reset before every test.
    [Fact]
    public async Task Fixture_table_is_in_place_for_every_test()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT code, lat, lon, retired FROM postcodes ORDER BY code", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<FixturePostcode>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new FixturePostcode(reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetBoolean(3)));
        }

        Assert.Equal(FixturePostcodes.All.OrderBy(postcode => postcode.Code, StringComparer.Ordinal), rows);
        Assert.Single(rows, row => row.Retired);
    }

    private async Task<PointProbe?> PointAsync(string code)
    {
        using var response = await PostAsync(code);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PointProbe>(TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> PostAsync(string code) =>
        Factory.CreateClient().PostAsJsonAsync(
            $"{TestEndpoints.Prefix}/postcodes/point", new ProbeValue(code), TestContext.Current.CancellationToken);
}

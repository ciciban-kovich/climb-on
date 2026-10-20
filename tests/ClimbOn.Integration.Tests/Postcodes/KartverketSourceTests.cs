using System.Net.Http.Json;
using ClimbOn.Api.Jobs;
using ClimbOn.Domain.Geo;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Postcodes;

// The real Kartverket format (C2), imported from an unmodified excerpt of the dataset and read back.
public sealed class KartverketSourceTests(ApiFactory factory) : IntegrationTest(factory)
{
    // The stored point, read back through the api, against coordinates from outside the dataset.
    [Theory]
    [Trait("Spec", "S99")]
    // Det kongelige slott, Oslo: en.wikipedia.org/wiki/Royal_Palace,_Oslo (59°55′2″N 10°43′39″E).
    [InlineData("0010", 59.91722, 10.72750)]
    // Bærums Verk: en.wikipedia.org/wiki/Bærums_Verk (59°56′35″N 10°30′27″E).
    [InlineData("1354", 59.94306, 10.50750)]
    public async Task Known_postcode_matches_independent_latlon(string code, double latitude, double longitude)
    {
        await using var table = await ImportExcerptAsync();

        var stored = await PointAsync(code);

        Assert.InRange(Distance.Kilometres(new GeoPoint(stored.Latitude, stored.Longitude), new GeoPoint(latitude, longitude)), 0, 5);
    }

    // A projected easting/northing (EPSG:25833) stored as latitude/longitude fails both ranges.
    [Fact]
    [Trait("Spec", "S99")]
    public async Task Every_point_is_geographic_and_in_Norway()
    {
        await using var table = await ImportExcerptAsync();

        var rows = await Factory.CreateClient().GetFromJsonAsync<List<FixturePostcode>>(
            $"{TestEndpoints.Prefix}/postcodes", TestContext.Current.CancellationToken);

        Assert.Equal(8, rows!.Count);
        Assert.All(rows, row =>
        {
            Assert.InRange(row.Latitude, 57, 72);
            Assert.InRange(row.Longitude, 4, 32);
        });
    }

    private async Task<IAsyncDisposable> ImportExcerptAsync()
    {
        var table = await FixturePostcodes.ClearForImportAsync(Factory.ConnectionString, TestContext.Current.CancellationToken);
        try
        {
            Factory.Kartverket.Serve(KartverketFixture.Excerpt);
            Assert.Equal(0, await Factory.RunJobAsync(PostcodesJob.JobName));
            return table;
        }
        catch
        {
            await table.DisposeAsync();
            throw;
        }
    }

    private async Task<PointProbe> PointAsync(string code)
    {
        using var response = await Factory.CreateClient().PostAsJsonAsync(
            $"{TestEndpoints.Prefix}/postcodes/point", new ProbeValue(Factory.Data.Postcode(code)), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PointProbe>(TestContext.Current.CancellationToken))!;
    }
}

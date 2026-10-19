using System.Net.Http.Json;
using ClimbOn.Api.Jobs;
using ClimbOn.Domain.Geo;
using ClimbOn.Infrastructure.Postcodes;
using ClimbOn.Integration.Tests.Harness;
using NetTopologySuite.Geometries;
using Xunit;

namespace ClimbOn.Integration.Tests.Postcodes;

// The real Kartverket format (C2), read from an unmodified excerpt of the dataset.
public sealed class KartverketSourceTests(ApiFactory factory) : IntegrationTest(factory)
{
    // Its centroid lies outside its area.
    private const string Concave = "5003";

    [Fact]
    [Trait("SpecPartial", "S101")]
    public async Task Parses_real_dataset_excerpt()
    {
        var areas = await ReadAsync(KartverketFixture.Excerpt);

        Assert.Equal(
            ["0010", "0159", "1063", "1354", "4006", "5003", "7010", "9008"],
            areas.Select(area => area.Code).Order(StringComparer.Ordinal));

        // Two features each in the file; 0159 also has a hole.
        foreach (var code in new[] { "0159", "1063" })
        {
            var area = Assert.IsType<MultiPolygon>(areas.Single(area => area.Code == code).Area);
            Assert.Equal(2, area.NumGeometries);
        }

        Assert.Contains(areas.Select(area => area.Area).OfType<MultiPolygon>().SelectMany(multi => multi.Geometries.Cast<Polygon>()), polygon => polygon.NumInteriorRings > 0);
    }

    [Fact]
    [Trait("Spec", "S99")]
    public async Task Reference_point_lies_inside_area()
    {
        var areas = await ReadAsync(KartverketFixture.Excerpt);

        Assert.All(areas, area => Assert.True(area.Area.Contains(area.ReferencePoint), area.Code));
        var concave = areas.Single(area => area.Code == Concave).Area;
        Assert.False(concave.Contains(concave.Centroid));
    }

    [Fact]
    [Trait("Spec", "S100")]
    public async Task Feature_without_an_area_is_left_out()
    {
        var areas = await ReadAsync(KartverketFixture.NextVersion);

        Assert.DoesNotContain(areas, area => area.Code == "0101");
    }

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

    private static async Task<IReadOnlyList<KartverketArea>> ReadAsync(string file)
    {
        await using var geojson = File.OpenRead(KartverketFixture.PathOf(file));
        return await KartverketSource.ReadAreasAsync(geojson, TestContext.Current.CancellationToken);
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

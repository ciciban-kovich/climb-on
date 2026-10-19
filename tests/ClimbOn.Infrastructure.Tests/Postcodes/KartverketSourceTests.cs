using System.Text;
using ClimbOn.Infrastructure.Postcodes;
using NetTopologySuite.Geometries;
using Xunit;
using static ClimbOn.Infrastructure.Tests.Postcodes.KartverketFiles;

namespace ClimbOn.Infrastructure.Tests.Postcodes;

// Reading the Kartverket format (C2). Each refused dataset differs from the accepted one in
// Hand_written_dataset_is_read in one place only.
public sealed class KartverketSourceTests
{
    // Its centroid lies outside its area.
    private const string Concave = "5003";

    [Fact]
    [Trait("SpecPartial", "S101")]
    public async Task Parses_real_dataset_excerpt()
    {
        var areas = await ReadFileAsync(Excerpt);

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
        var areas = await ReadFileAsync(Excerpt);

        Assert.All(areas, area => Assert.True(area.Area.Contains(area.ReferencePoint), area.Code));
        var concave = areas.Single(area => area.Code == Concave).Area;
        Assert.False(concave.Contains(concave.Centroid));
    }

    [Fact]
    [Trait("Spec", "S100")]
    public async Task Feature_without_an_area_is_left_out()
    {
        var areas = await ReadFileAsync(NextVersion);

        Assert.DoesNotContain(areas, area => area.Code == "0101");
    }

    [Fact]
    [Trait("SpecPartial", "S101")]
    public async Task Hand_written_dataset_is_read()
    {
        var area = Assert.Single(await ReadAsync(Dataset(Feature())));

        Assert.Equal("0010", area.Code);
        Assert.True(area.Area.Contains(area.ReferencePoint));
    }

    // Read as degrees, a projected easting/northing would put every point in the wrong place.
    [Theory]
    [Trait("SpecPartial", "S99")]
    [InlineData("\"EPSG:25833\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public async Task Coordinates_in_another_crs_are_refused(string crs) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(DatasetIn(crs, Feature())));

    [Fact]
    [Trait("SpecPartial", "S101")]
    public async Task Dataset_without_its_area_collection_is_refused() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync("""{ "postnummeromrader.postnummeromradegrense": {} }"""));

    // Imported, an empty dataset would retire every postcode (S102).
    [Theory]
    [Trait("SpecPartial", "S102")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dataset_without_any_area_is_refused(bool withAreaLessFeature)
    {
        var features = withAreaLessFeature ? new[] { Feature(geometry: "null") } : [];

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(Dataset(features)));
    }

    [Theory]
    [Trait("SpecPartial", "S101")]
    [InlineData("\"010\"")]
    [InlineData("\"00100\"")]
    [InlineData("\"001a\"")]
    [InlineData("10")]
    [InlineData("null")]
    public async Task Area_without_a_four_digit_postcode_is_refused(string postnummer) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(Dataset(Feature(postnummer))));

    [Theory]
    [Trait("SpecPartial", "S101")]
    [InlineData("""{ "type": "LineString", "coordinates": [[10, 60], [10.1, 60.1]] }""")]
    [InlineData("""{ "type": "Point", "coordinates": [10, 60] }""")]
    public async Task Area_that_is_not_a_polygon_is_refused(string geometry) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(Dataset(Feature(geometry: geometry))));

    private static async Task<IReadOnlyList<KartverketArea>> ReadFileAsync(string file)
    {
        await using var geojson = File.OpenRead(PathOf(file));
        return await KartverketSource.ReadAreasAsync(geojson, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<KartverketArea>> ReadAsync(string json)
    {
        await using var geojson = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await KartverketSource.ReadAreasAsync(geojson, TestContext.Current.CancellationToken);
    }
}

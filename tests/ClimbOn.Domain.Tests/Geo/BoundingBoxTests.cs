using ClimbOn.Domain.Geo;
using Xunit;

namespace ClimbOn.Domain.Tests.Geo;

// The prefilter's box (design §2 'Matching') must hold every point within the radius, or a candidate would be lost.
public sealed class BoundingBoxTests
{
    [Theory]
    [InlineData(59.9139, 10.7522, 1.5)]
    [InlineData(59.9139, 10.7522, 100.5)]
    [InlineData(71.1685, 25.7838, 100.5)]
    [InlineData(57.9, 4.5, 50)]
    [InlineData(-33.9, 151.2, 25)]
    [InlineData(0, 179.9, 30)]
    [InlineData(89.9, 0, 30)]
    public void Holds_every_point_within_the_radius(double latitude, double longitude, double radiusKm)
    {
        var centre = new GeoPoint(latitude, longitude);
        var box = BoundingBox.Around(centre, radiusKm);

        var outside = Enumerable.Range(0, 3600)
            .Select(step => PointAt(centre, step / 10.0, radiusKm * 0.999999))
            .Where(point => !box.Contains(point))
            .ToList();

        Assert.Empty(outside);
    }

    [Fact]
    public void Is_not_much_larger_than_the_radius()
    {
        var centre = new GeoPoint(59.9139, 10.7522);
        var box = BoundingBox.Around(centre, 10.5);

        Assert.False(box.Contains(PointAt(centre, 0, 10.6)));
        Assert.False(box.Contains(PointAt(centre, 90, 10.6)));
        Assert.False(box.Contains(PointAt(centre, 180, 10.6)));
        Assert.False(box.Contains(PointAt(centre, 270, 10.6)));
    }

    // The point at the given bearing (degrees from north) and great-circle distance from start.
    private static GeoPoint PointAt(GeoPoint start, double bearingDegrees, double km)
    {
        var angle = km / Distance.EarthRadiusKm;
        var bearing = bearingDegrees * Math.PI / 180;
        var latitude = start.Latitude * Math.PI / 180;
        var longitude = start.Longitude * Math.PI / 180;
        var toLatitude = Math.Asin((Math.Sin(latitude) * Math.Cos(angle)) + (Math.Cos(latitude) * Math.Sin(angle) * Math.Cos(bearing)));
        var toLongitude = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angle) * Math.Cos(latitude), Math.Cos(angle) - (Math.Sin(latitude) * Math.Sin(toLatitude)));
        var degrees = toLongitude * 180 / Math.PI;
        degrees = ((degrees + 540) % 360) - 180;
        return new GeoPoint(Math.Clamp(toLatitude * 180 / Math.PI, -90, 90), degrees);
    }
}

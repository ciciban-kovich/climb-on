namespace ClimbOn.Domain.Geo;

// A latitude/longitude box holding every point within a radius of a centre, for the prefilter of design §2
// 'Matching'. It may hold more: it is a superset, never a subset, of the points Distance.Kilometres puts within
// the radius.
public sealed record BoundingBox(double South, double North, double West, double East)
{
    // Edges are rounded outward to this many degrees (about 11 m of latitude), plus one more step, so that the
    // floating-point product never lands back inside the exact edge.
    private const double Step = 0.0001;

    public static BoundingBox Around(GeoPoint centre, double radiusKm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusKm);

        var angle = radiusKm / Distance.EarthRadiusKm;
        var south = centre.Latitude - Degrees(angle);
        var north = centre.Latitude + Degrees(angle);

        // The widest longitude a point within the radius reaches, at the latitude where it is widest. Beyond a
        // pole, or across the antimeridian, every longitude is in.
        var reach = Math.Sin(angle) / Math.Cos(Radians(centre.Latitude));
        if (south <= -90 || north >= 90 || angle >= Math.PI / 2 || reach >= 1)
        {
            return new BoundingBox(Math.Max(-90, Down(south)), Math.Min(90, Up(north)), -180, 180);
        }

        var spread = Degrees(Math.Asin(reach));
        var west = centre.Longitude - spread;
        var east = centre.Longitude + spread;
        return west <= -180 || east >= 180
            ? new BoundingBox(Down(south), Up(north), -180, 180)
            : new BoundingBox(Down(south), Up(north), Down(west), Up(east));
    }

    public bool Contains(GeoPoint point) =>
        point.Latitude >= South && point.Latitude <= North && point.Longitude >= West && point.Longitude <= East;

    private static double Down(double degrees) => (Math.Floor(degrees / Step) - 1) * Step;

    private static double Up(double degrees) => (Math.Ceiling(degrees / Step) + 1) * Step;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

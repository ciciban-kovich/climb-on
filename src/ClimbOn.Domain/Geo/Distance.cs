namespace ClimbOn.Domain.Geo;

// S16: straight-line (great-circle) distance between two reference points, by the haversine formula.
public static class Distance
{
    // The mean earth radius (IUGG).
    public const double EarthRadiusKm = 6371.0088;

    public static double Kilometres(GeoPoint from, GeoPoint to)
    {
        var deltaLatitude = Radians(to.Latitude - from.Latitude);
        var deltaLongitude = Radians(to.Longitude - from.Longitude);
        var h = Square(Math.Sin(deltaLatitude / 2))
            + (Math.Cos(Radians(from.Latitude)) * Math.Cos(Radians(to.Latitude)) * Square(Math.Sin(deltaLongitude / 2)));

        // Rounding can carry h just past 1 for antipodal points; Asin would then return NaN.
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(Math.Min(1, h)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Square(double value) => value * value;
}

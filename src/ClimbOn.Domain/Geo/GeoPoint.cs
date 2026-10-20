namespace ClimbOn.Domain.Geo;

// A point on the earth in decimal degrees (WGS 84).
public readonly record struct GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (!(latitude is >= -90 and <= 90))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, null);
        }

        if (!(longitude is >= -180 and <= 180))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, null);
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}

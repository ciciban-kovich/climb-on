namespace ClimbOn.Infrastructure.Persistence;

// A row of the postcode reference table (S99, S102).
public sealed class Postcode
{
    public required string Code { get; init; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public bool Retired { get; set; }
}

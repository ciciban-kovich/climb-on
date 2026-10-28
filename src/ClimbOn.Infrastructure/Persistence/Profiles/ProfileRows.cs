namespace ClimbOn.Infrastructure.Persistence.Profiles;

// A row of gyms (S17). Only the admin creates gyms (M2); until then they exist only as test fixtures.
public sealed class GymRow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Address { get; init; }

    public required string Postcode { get; init; }

    public required bool Active { get; init; }
}

// A row of profiles: the S3 values held once per profile. Values are WireNames.
public sealed class ProfileRow
{
    public required Guid AccountId { get; init; }

    public required string DisplayName { get; init; }

    public required string Sex { get; init; }

    public required string[] AcceptedSexes { get; init; }

    public required string RopeScale { get; init; }

    public required string Certification { get; init; }

    public required bool OwnGear { get; init; }

    // Kept as stored even when retired (S102.3), so no foreign key to postcodes.
    public required string Postcode { get; init; }

    public required int MaxTravelKm { get; init; }
}

// A row of discipline_variants: one variant of a profile, its grades as entered with their scale and
// their positions on the internal scale (S13.3). Shared marks the variants of one discipline that share
// their ranges (S3).
public sealed class DisciplineVariantRow
{
    public required Guid AccountId { get; init; }

    public required string Variant { get; init; }

    public required bool Shared { get; init; }

    public required string Scale { get; init; }

    public required string OwnLowest { get; init; }

    public required int OwnLowestPosition { get; init; }

    public required string OwnHighest { get; init; }

    public required int OwnHighestPosition { get; init; }

    public required string PartnerLowest { get; init; }

    public required int PartnerLowestPosition { get; init; }

    public required string PartnerHighest { get; init; }

    public required int PartnerHighestPosition { get; init; }
}

// A row of availability_windows. Day is a WireName of DayOfWeek; hours are whole, an end of 24 the end of the day.
public sealed class AvailabilityWindowRow
{
    public required Guid AccountId { get; init; }

    public required string Day { get; init; }

    public required int StartHour { get; init; }

    public required int EndHour { get; init; }
}

// A row of home_gyms. Position keeps the order the climber gave.
public sealed class HomeGymRow
{
    public required Guid AccountId { get; init; }

    public required Guid GymId { get; init; }

    public required int Position { get; init; }
}

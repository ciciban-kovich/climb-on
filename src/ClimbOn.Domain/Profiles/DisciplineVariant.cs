using ClimbOn.Domain.Grades;

namespace ClimbOn.Domain.Profiles;

public enum Discipline
{
    Bouldering,
    TopRope,
    Lead,
    Multipitch,
    Ice,
}

public enum Venue
{
    Indoor,
    Outdoor,
}

// The eight variants of the specs glossary.
public enum DisciplineVariant
{
    BoulderingIndoor,
    BoulderingOutdoor,
    TopRopeIndoor,
    TopRopeOutdoor,
    LeadIndoor,
    LeadOutdoor,
    MultipitchOutdoor,
    IceOutdoor,
}

// S3
public enum BelayCertification
{
    None,
    BrattkortTopptau,
    BrattkortLed,
}

public static class DisciplineVariants
{
    public static IReadOnlyList<DisciplineVariant> All { get; } = Enum.GetValues<DisciplineVariant>();

    public static Discipline Discipline(this DisciplineVariant variant) => variant switch
    {
        DisciplineVariant.BoulderingIndoor or DisciplineVariant.BoulderingOutdoor => Profiles.Discipline.Bouldering,
        DisciplineVariant.TopRopeIndoor or DisciplineVariant.TopRopeOutdoor => Profiles.Discipline.TopRope,
        DisciplineVariant.LeadIndoor or DisciplineVariant.LeadOutdoor => Profiles.Discipline.Lead,
        DisciplineVariant.MultipitchOutdoor => Profiles.Discipline.Multipitch,
        DisciplineVariant.IceOutdoor => Profiles.Discipline.Ice,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    public static Venue Venue(this DisciplineVariant variant) => variant switch
    {
        DisciplineVariant.BoulderingIndoor or DisciplineVariant.TopRopeIndoor or DisciplineVariant.LeadIndoor => Profiles.Venue.Indoor,
        DisciplineVariant.BoulderingOutdoor or DisciplineVariant.TopRopeOutdoor or DisciplineVariant.LeadOutdoor
            or DisciplineVariant.MultipitchOutdoor or DisciplineVariant.IceOutdoor => Profiles.Venue.Outdoor,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    // Null for a pair that is not a variant, such as multipitch indoor.
    public static DisciplineVariant? Of(Discipline discipline, Venue venue) =>
        All.Where(variant => variant.Discipline() == discipline && variant.Venue() == venue)
            .Select(variant => (DisciplineVariant?)variant)
            .SingleOrDefault();

    // S13.1, S13.2
    public static GradeFamily GradeFamily(this Discipline discipline) => discipline switch
    {
        Profiles.Discipline.TopRope or Profiles.Discipline.Lead or Profiles.Discipline.Multipitch => Grades.GradeFamily.Rope,
        Profiles.Discipline.Bouldering => Grades.GradeFamily.Bouldering,
        Profiles.Discipline.Ice => Grades.GradeFamily.Ice,
        _ => throw new ArgumentOutOfRangeException(nameof(discipline), discipline, null),
    };

    // S12
    public static bool IsMetBy(this DisciplineVariant variant, BelayCertification certification)
    {
        if (!Enum.IsDefined(certification))
        {
            throw new ArgumentOutOfRangeException(nameof(certification), certification, null);
        }

        return variant switch
        {
            DisciplineVariant.TopRopeIndoor => certification is BelayCertification.BrattkortTopptau or BelayCertification.BrattkortLed,
            DisciplineVariant.LeadIndoor => certification is BelayCertification.BrattkortLed,
            _ when Enum.IsDefined(variant) => true,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
    }
}

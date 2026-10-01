using System.Diagnostics.CodeAnalysis;

namespace ClimbOn.Domain.Grades;

// S13.3: a grade as entered plus its position on the internal scale of its family.
public sealed record Grade
{
    private Grade(GradeScale scale, string entered, int position)
    {
        Scale = scale;
        Entered = entered;
        Position = position;
    }

    public GradeScale Scale { get; }

    public string Entered { get; }

    public int Position { get; }

    public GradeFamily Family => Scale.Family();

    // S13.4
    public string OwnerView => Entered;

    public static bool TryCreate(GradeScale scale, string? entered, [NotNullWhen(true)] out Grade? grade)
    {
        grade = null;
        if (entered is null || GradeScales.PositionOf(scale, entered) is not { } position)
        {
            return false;
        }

        grade = new Grade(scale, entered, position);
        return true;
    }

    // S13.5: viewerRopeScale is the viewing climber's chosen rope scale (S13.1).
    public string ShownTo(GradeScale viewerRopeScale)
    {
        if (viewerRopeScale.Family() != GradeFamily.Rope)
        {
            throw new ArgumentOutOfRangeException(nameof(viewerRopeScale), viewerRopeScale, "not a rope scale");
        }

        return Family != GradeFamily.Rope || viewerRopeScale == Scale
            ? Entered
            : RopeConversionTable.GradeAt(viewerRopeScale, Position);
    }
}

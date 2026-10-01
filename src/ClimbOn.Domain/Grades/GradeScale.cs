namespace ClimbOn.Domain.Grades;

public enum GradeScale
{
    Norwegian,
    French,
    Font,
    WaterIce,
}

public enum GradeFamily
{
    Rope,
    Bouldering,
    Ice,
}

public static class GradeScales
{
    private static readonly string[] Font =
    [
        "3", "4", "4+", "5", "5+",
        "6A", "6A+", "6B", "6B+", "6C", "6C+",
        "7A", "7A+", "7B", "7B+", "7C", "7C+",
        "8A", "8A+", "8B", "8B+", "8C", "8C+",
        "9A",
    ];

    private static readonly string[] WaterIce =
        ["WI1", "WI2", "WI3", "WI3+", "WI4", "WI4+", "WI5", "WI5+", "WI6", "WI6+", "WI7"];

    private static readonly IReadOnlyList<string> FontGrades = Array.AsReadOnly(Font);

    private static readonly IReadOnlyList<string> WaterIceGrades = Array.AsReadOnly(WaterIce);

    public static GradeFamily Family(this GradeScale scale) => scale switch
    {
        GradeScale.Norwegian or GradeScale.French => GradeFamily.Rope,
        GradeScale.Font => GradeFamily.Bouldering,
        GradeScale.WaterIce => GradeFamily.Ice,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, null),
    };

    // S13.1, S13.2
    public static IReadOnlyList<GradeScale> For(GradeFamily family) => family switch
    {
        GradeFamily.Rope => [GradeScale.Norwegian, GradeScale.French],
        GradeFamily.Bouldering => [GradeScale.Font],
        GradeFamily.Ice => [GradeScale.WaterIce],
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
    };

    // Easiest first.
    public static IReadOnlyList<string> Grades(this GradeScale scale) => scale switch
    {
        GradeScale.Norwegian => RopeConversionTable.NorwegianGrades,
        GradeScale.French => RopeConversionTable.FrenchGrades,
        GradeScale.Font => FontGrades,
        GradeScale.WaterIce => WaterIceGrades,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, null),
    };

    internal static int? PositionOf(GradeScale scale, string grade)
    {
        if (scale.Family() == GradeFamily.Rope)
        {
            return RopeConversionTable.PositionOf(scale, grade);
        }

        var index = Array.IndexOf(scale == GradeScale.Font ? Font : WaterIce, grade);
        return index < 0 ? null : index;
    }
}

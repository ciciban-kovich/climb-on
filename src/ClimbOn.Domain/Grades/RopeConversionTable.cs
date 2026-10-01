namespace ClimbOn.Domain.Grades;

// S13.3: the single fixed table. A row's index is a position on the internal rope scale; a grade
// sits at the first row it appears in.
public static class RopeConversionTable
{
    private static readonly (string Norwegian, string French)[] Rows =
    [
        ("3", "3"),
        ("4-", "4a"),
        ("4", "4b"),
        ("4+", "4c"),
        ("5-", "5a"),
        ("5", "5b"),
        ("5+", "5c"),
        ("6-", "6a"),
        ("6", "6a"),
        ("6+", "6a+"),
        ("7-", "6b"),
        ("7-", "6b+"),
        ("7", "6c"),
        ("7+", "6c+"),
        ("8-", "7a"),
        ("8", "7a+"),
        ("8", "7b"),
        ("8+", "7b+"),
        ("9-", "7c"),
        ("9", "7c+"),
        ("9", "8a"),
        ("9+", "8a+"),
        ("10-", "8b"),
        ("10", "8b+"),
        ("10", "8c"),
        ("10+", "8c+"),
        ("11-", "9a"),
        ("11", "9a+"),
        ("11", "9b"),
        ("11+", "9b+"),
        ("11+", "9c"),
    ];

    public static IReadOnlyList<string> NorwegianGrades { get; } = Array.AsReadOnly(Rows.Select(r => r.Norwegian).Distinct().ToArray());

    public static IReadOnlyList<string> FrenchGrades { get; } = Array.AsReadOnly(Rows.Select(r => r.French).Distinct().ToArray());

    public static int? PositionOf(GradeScale scale, string grade)
    {
        for (var position = 0; position < Rows.Length; position++)
        {
            if (Cell(scale, position) == grade)
            {
                return position;
            }
        }

        return null;
    }

    public static string GradeAt(GradeScale scale, int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Rows.Length);
        return Cell(scale, position);
    }

    private static string Cell(GradeScale scale, int position) => scale switch
    {
        GradeScale.Norwegian => Rows[position].Norwegian,
        GradeScale.French => Rows[position].French,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "not a rope scale"),
    };
}

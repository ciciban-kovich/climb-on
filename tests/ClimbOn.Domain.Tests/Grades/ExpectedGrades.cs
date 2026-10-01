using ClimbOn.Domain.Grades;

namespace ClimbOn.Domain.Tests.Grades;

// Each scale's grades, easiest first, written out independently of the code under test.
internal static class ExpectedGrades
{
    public static IReadOnlyList<string> For(GradeScale scale) => scale switch
    {
        GradeScale.Norwegian =>
        [
            "3", "4-", "4", "4+", "5-", "5", "5+", "6-", "6", "6+", "7-", "7", "7+",
            "8-", "8", "8+", "9-", "9", "9+", "10-", "10", "10+", "11-", "11", "11+",
        ],
        GradeScale.French =>
        [
            "3", "4a", "4b", "4c", "5a", "5b", "5c", "6a", "6a+", "6b", "6b+", "6c", "6c+",
            "7a", "7a+", "7b", "7b+", "7c", "7c+", "8a", "8a+", "8b", "8b+", "8c", "8c+",
            "9a", "9a+", "9b", "9b+", "9c",
        ],
        GradeScale.Font =>
        [
            "3", "4", "4+", "5", "5+",
            "6A", "6A+", "6B", "6B+", "6C", "6C+",
            "7A", "7A+", "7B", "7B+", "7C", "7C+",
            "8A", "8A+", "8B", "8B+", "8C", "8C+",
            "9A",
        ],
        GradeScale.WaterIce => ["WI1", "WI2", "WI3", "WI3+", "WI4", "WI4+", "WI5", "WI5+", "WI6", "WI6+", "WI7"],
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, null),
    };
}

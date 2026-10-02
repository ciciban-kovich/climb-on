namespace ClimbOn.Domain.Tests.Grades;

// The rope conversion table, one row per internal position, written out independently of the code under test.
internal static class ExpectedConversionTable
{
    public static IReadOnlyList<(string Norwegian, string French)> Rows { get; } =
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
}

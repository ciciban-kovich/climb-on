using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using Xunit;

namespace ClimbOn.Domain.Tests.Accounts;

[Trait("SpecPartial", "S28")]
public sealed class AgePolicyTests
{
    [Theory]
    [InlineData("2008-01-15", "2026-01-15", 18)]
    [InlineData("2008-01-16", "2026-01-15", 17)]
    [InlineData("2008-01-14", "2026-01-15", 18)]
    [InlineData("2008-12-31", "2026-12-30", 17)]
    [InlineData("2008-12-31", "2026-12-31", 18)]
    [InlineData("2026-01-15", "2026-01-15", 0)]
    [InlineData("2026-01-16", "2026-01-15", -1)]
    [InlineData("2008-02-29", "2026-02-28", 17)]
    [InlineData("2008-02-29", "2026-03-01", 18)]
    [InlineData("2008-02-29", "2028-02-29", 20)]
    [InlineData("1930-06-01", "2026-06-01", 96)]
    public void Age_is_counted_in_completed_years(string dateOfBirth, string today, int age) =>
        Assert.Equal(age, AgePolicy.AgeOn(Date(dateOfBirth), Date(today)));

    [Fact]
    public void Eighteenth_birthday_is_accepted() =>
        AgePolicy.EnsureAdult(new DateOnly(2008, 1, 15), new DateOnly(2026, 1, 15));

    [Fact]
    public void Day_before_eighteenth_birthday_is_rejected()
    {
        var error = Assert.Throws<DomainError>(() => AgePolicy.EnsureAdult(new DateOnly(2008, 1, 16), new DateOnly(2026, 1, 15)));

        Assert.Equal(AgePolicy.UnderageCode, error.Code);
    }

    [Fact]
    public void Born_on_29_february_turns_18_on_1_march_in_a_common_year()
    {
        var dateOfBirth = new DateOnly(2008, 2, 29);

        Assert.Throws<DomainError>(() => AgePolicy.EnsureAdult(dateOfBirth, new DateOnly(2026, 2, 28)));
        AgePolicy.EnsureAdult(dateOfBirth, new DateOnly(2026, 3, 1));
    }

    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", null);
}

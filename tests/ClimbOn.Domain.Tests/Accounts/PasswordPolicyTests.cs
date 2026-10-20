using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using Xunit;

namespace ClimbOn.Domain.Tests.Accounts;

[Trait("SpecPartial", "S79")]
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(127)]
    [InlineData(128)]
    public void Length_10_to_128_is_accepted(int length) =>
        PasswordPolicy.Ensure(new string('a', length));

    [Theory]
    [InlineData(0, PasswordPolicy.TooShortCode)]
    [InlineData(9, PasswordPolicy.TooShortCode)]
    [InlineData(129, PasswordPolicy.TooLongCode)]
    [InlineData(1000, PasswordPolicy.TooLongCode)]
    public void Length_outside_10_to_128_is_rejected(int length, string code) =>
        Assert.Equal(code, Assert.Throws<DomainError>(() => PasswordPolicy.Ensure(new string('a', length))).Code);

    [Fact]
    public void Missing_password_is_too_short() =>
        Assert.Equal(PasswordPolicy.TooShortCode, Assert.Throws<DomainError>(() => PasswordPolicy.Ensure(null)).Code);

    // No composition rules: digits, uppercase or symbols are never required.
    [Theory]
    [InlineData("aaaaaaaaaa")]
    [InlineData("1234567890")]
    [InlineData("          ")]
    [InlineData("æøåæøåæøåæ")]
    public void No_composition_rules(string password) => PasswordPolicy.Ensure(password);

    // A character outside the Basic Multilingual Plane is two UTF-16 units but one character.
    [Theory]
    [InlineData(9, PasswordPolicy.TooShortCode)]
    [InlineData(10, null)]
    [InlineData(128, null)]
    [InlineData(129, PasswordPolicy.TooLongCode)]
    public void Characters_are_code_points(int count, string? code)
    {
        var password = string.Concat(Enumerable.Repeat("\U0001F9D7", count));

        var error = Record.Exception(() => PasswordPolicy.Ensure(password));

        Assert.Equal(code, (error as DomainError)?.Code);
    }
}

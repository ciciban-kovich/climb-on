using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Postcodes;
using Xunit;

namespace ClimbOn.Domain.Tests.Postcodes;

[Trait("SpecPartial", "S15")]
public sealed class PostcodeRulesTests
{
    private static readonly GeoPoint Point = new(60, 10);

    [Theory]
    [InlineData("0010")]
    [InlineData("9991")]
    public void Four_ascii_digits_are_well_formed(string postcode)
    {
        Assert.True(PostcodeRules.IsWellFormed(postcode));
        PostcodeRules.EnsureWellFormed(postcode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("015")]
    [InlineData("01500")]
    [InlineData("015a")]
    [InlineData(" 0150")]
    [InlineData("0150 ")]
    [InlineData("01 50")]
    [InlineData("-150")]
    [InlineData("０１５０")] // full-width digits
    [InlineData("٠١٥٠")] // Arabic-Indic digits
    public void Anything_else_is_malformed(string? postcode)
    {
        Assert.False(PostcodeRules.IsWellFormed(postcode));
        Assert.Equal(PostcodeRules.MalformedCode, Assert.Throws<DomainError>(() => PostcodeRules.EnsureWellFormed(postcode)).Code);
    }

    [Fact]
    public void Active_postcode_is_accepted() =>
        Assert.Equal("0010", PostcodeRules.Accept(new StoredPostcode("0010", Point, Retired: false)));

    [Fact]
    public void Postcode_not_in_the_table_is_rejected() =>
        Assert.Equal(PostcodeRules.UnknownCode, Assert.Throws<DomainError>(() => PostcodeRules.Accept(null)).Code);

    [Fact]
    [Trait("SpecPartial", "S102.2")]
    public void Retired_postcode_is_rejected() =>
        Assert.Equal(
            PostcodeRules.RetiredCode,
            Assert.Throws<DomainError>(() => PostcodeRules.Accept(new StoredPostcode("0010", Point, Retired: true))).Code);

    [Fact]
    public void Error_codes_are_distinct() =>
        Assert.Equal(PostcodeRules.Codes.Count, PostcodeRules.Codes.Distinct(StringComparer.Ordinal).Count());
}

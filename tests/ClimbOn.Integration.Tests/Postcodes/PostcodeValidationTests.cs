using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Postcodes;
using ClimbOn.Domain.Accounts;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Postcodes;

[Trait("SpecPartial", "S15")]
public sealed class PostcodeValidationTests(ApiFactory factory) : IntegrationTest(factory)
{
    public static TheoryData<string> ActivePostcodes =>
        [.. FixturePostcodes.All.Where(postcode => !postcode.Retired).Select(postcode => postcode.Code)];

    [Theory]
    [MemberData(nameof(ActivePostcodes))]
    public async Task Active_postcode_is_accepted(string code)
    {
        using var response = await ValidateAsync(Factory.Data.Postcode(code));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S102.2")]
    public async Task Retired_postcode_is_rejected()
    {
        Assert.True(FixturePostcodes.Get(FixturePostcodes.Retired).Retired);

        using var response = await ValidateAsync(Factory.Data.Postcode(FixturePostcodes.Retired));

        await AssertRejectedAsync(response, PostcodeValidator.RetiredCode);
    }

    [Fact]
    public async Task Postcode_not_in_the_table_is_rejected()
    {
        Assert.DoesNotContain(FixturePostcodes.All, postcode => postcode.Code == FixturePostcodes.Unknown);

        using var response = await ValidateAsync(Factory.Data.Postcode(FixturePostcodes.Unknown));

        await AssertRejectedAsync(response, PostcodeValidator.UnknownCode);
    }

    [Theory]
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
    public async Task Malformed_postcode_is_rejected(string value)
    {
        using var response = await ValidateAsync(value);

        await AssertRejectedAsync(response, PostcodeValidator.MalformedCode);
    }

    // C46 checks the two files agree; this checks the codes are among their keys.
    [Fact]
    public void Every_postcode_error_code_has_a_text_in_both_languages() =>
        Assert.Empty(PostcodeValidator.Codes.SelectMany(code => new[] { Language.English, Language.Bokmal }
            .Where(language => Texts.Get(code, language) == code)
            .Select(language => $"{code} ({language})")));

    private Task<HttpResponseMessage> ValidateAsync(string value) =>
        Factory.CreateClient().PostAsJsonAsync(
            $"{TestEndpoints.Prefix}/postcodes/validate", new ProbeValue(value), TestContext.Current.CancellationToken);

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(code, body?.Code);
    }
}

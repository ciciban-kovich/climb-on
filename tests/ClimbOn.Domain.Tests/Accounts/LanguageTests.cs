using ClimbOn.Domain.Accounts;
using Xunit;

namespace ClimbOn.Domain.Tests.Accounts;

[Trait("SpecPartial", "S104")]
public sealed class LanguageTests
{
    [Theory]
    [InlineData("nb")]
    [InlineData("nn")]
    [InlineData("no")]
    [InlineData("NB")]
    [InlineData("nb-NO")]
    [InlineData("nn_NO")]
    [InlineData(" no ")]
    public void Norwegian_settings_give_bokmal(string tag) =>
        Assert.Equal(Language.Bokmal, Languages.FromClientSetting(tag));

    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("sv")]
    [InlineData("da")]
    [InlineData("nbx")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_gives_english(string? tag) =>
        Assert.Equal(Language.English, Languages.FromClientSetting(tag));

    [Theory]
    [InlineData(Language.English, "en")]
    [InlineData(Language.Bokmal, "nb")]
    public void Tags_are_resource_culture_names(Language language, string tag) =>
        Assert.Equal(tag, language.Tag());
}

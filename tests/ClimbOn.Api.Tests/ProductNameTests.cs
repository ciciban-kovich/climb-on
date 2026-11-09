using System.Text.RegularExpressions;
using Xunit;

namespace ClimbOn.Api.Tests;

// The product is "Climb On" in every text a climber reads: the api's messages and emails, the client's texts, its
// page title and its manifest. Not checked: code identifiers and file names (ClimbOn.*, climbon.js), and the legal
// texts, whose published versions never change (S112).
public sealed partial class ProductNameTests
{
    [Theory]
    [InlineData("src/ClimbOn.Api/Resources/Texts.resx")]
    [InlineData("src/ClimbOn.Api/Resources/Texts.nb.resx")]
    [InlineData("src/ClimbOn.Client/Resources/UiTexts.resx")]
    [InlineData("src/ClimbOn.Client/Resources/UiTexts.nb.resx")]
    [InlineData("src/ClimbOn.Client/wwwroot/index.html")]
    [InlineData("src/ClimbOn.Client/wwwroot/manifest.webmanifest")]
    public void Climber_facing_text_spells_the_name_Climb_On(string file)
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, file));

        Assert.Empty(OtherSpelling().Matches(text).Select(match => match.Value));
    }

    // climb-on, climbon, ClimbOn as a word, but not followed by a dot, as an identifier or a file name is.
    [GeneratedRegex(@"\bclimb-?on\b(?!\.)", RegexOptions.IgnoreCase)]
    private static partial Regex OtherSpelling();
}

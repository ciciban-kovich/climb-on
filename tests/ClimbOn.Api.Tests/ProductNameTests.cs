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

    [Theory]
    [InlineData("Someone tried to register at climb-on.")]
    [InlineData("Velkommen til ClimbOn.")]
    [InlineData("Welcome to climbon today")]
    [InlineData("Welcome to Climb on")]
    [InlineData("WELCOME TO CLIMB ON")]
    [InlineData("<title>Climb-On</title>")]
    public void Other_spellings_are_found(string text) =>
        Assert.Matches(OtherSpelling(), text);

    [Theory]
    [InlineData("Welcome to Climb On.")]
    [InlineData("<script src=\"js/climbon.js\"></script>")]
    [InlineData("ClimbOn.Api")]
    [InlineData("climbOn.storedLanguage")]
    [InlineData("Where do you climb on weekdays?")]
    public void The_name_identifiers_and_the_verb_are_not(string text) =>
        Assert.DoesNotMatch(OtherSpelling(), text);

    // climb-on, climbon or ClimbOn in any case, but not as an identifier or a file name (a dot and a letter follow);
    // or the two words in any case but "Climb On" itself and the verb "climb on".
    [GeneratedRegex(@"\b(?i:climb-?on)\b(?!\.\w)|\b(?!Climb On\b|climb on\b)(?i:climb on)\b")]
    private static partial Regex OtherSpelling();
}

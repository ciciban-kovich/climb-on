using System.Globalization;
using System.Resources;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Api.Resources;

// Texts.resx (English, the neutral language) and Texts.nb.resx (Bokmål). Keys are error codes and text keys.
public static class Texts
{
    public static ResourceManager Resources { get; } = new("ClimbOn.Api.Resources.Texts", typeof(Texts).Assembly);

    // ResourceParityTests (C46) keeps both files on one key set; a key missing from both returns the key itself,
    // so an error response never fails for want of a text.
    public static string Get(string key, Language language) =>
        Resources.GetString(key, CultureInfo.GetCultureInfo(language.Tag())) ?? key;
}

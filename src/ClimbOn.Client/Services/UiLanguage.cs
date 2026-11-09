using System.Globalization;

namespace ClimbOn.Client.Services;

// The language of the client's own texts: Resources/UiTexts.nb.resx or the neutral English one (S103).
public static class UiLanguage
{
    public const string English = "en";

    public const string Bokmal = "nb";

    // The language whose texts the culture shows: nb when its resource fallback reaches nb, otherwise English.
    public static string Of(CultureInfo culture)
    {
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            if (current.Name == Bokmal)
            {
                return Bokmal;
            }
        }

        return English;
    }

    // The climber's stored language once known (C62), until then the browser's; English if neither is a culture.
    public static CultureInfo StartupCulture(string? stored, string? browser)
    {
        foreach (var tag in new[] { stored, browser })
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            try
            {
                return CultureInfo.GetCultureInfo(tag);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.GetCultureInfo(English);
    }
}

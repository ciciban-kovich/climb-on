namespace ClimbOn.Domain.Accounts;

// S103
public enum Language
{
    English,
    Bokmal,
}

public static class Languages
{
    // S104: nb, nn or no gives Bokmål, anything else English. Only the primary subtag counts (nb-NO is nb).
    public static Language FromClientSetting(string? tag)
    {
        var primary = (tag ?? string.Empty).Split('-', '_')[0].Trim();
        return primary.ToLowerInvariant() is "nb" or "nn" or "no" ? Language.Bokmal : Language.English;
    }

    // The language's tag as used for resources and the Content-Language header.
    public static string Tag(this Language language) => language switch
    {
        Language.Bokmal => "nb",
        Language.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };
}

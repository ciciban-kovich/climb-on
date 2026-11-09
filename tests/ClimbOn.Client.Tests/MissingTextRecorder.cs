using ClimbOn.Client.Resources;
using Microsoft.Extensions.Localization;

namespace ClimbOn.Client.Tests;

// The client's localizer, recording every text a page asked for that the resources do not have.
public sealed class MissingTextRecorder(IStringLocalizerFactory factory) : IStringLocalizer<UiTexts>
{
    private readonly IStringLocalizer inner = factory.Create(typeof(UiTexts));

    private readonly List<string> missing = [];

    public IReadOnlyList<string> Missing
    {
        get
        {
            lock (missing)
            {
                return [.. missing];
            }
        }
    }

    public LocalizedString this[string name] => Record(inner[name]);

    public LocalizedString this[string name, params object[] arguments] => Record(inner[name, arguments]);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => inner.GetAllStrings(includeParentCultures);

    private LocalizedString Record(LocalizedString text)
    {
        if (text.ResourceNotFound)
        {
            lock (missing)
            {
                missing.Add(text.Name);
            }
        }

        return text;
    }
}

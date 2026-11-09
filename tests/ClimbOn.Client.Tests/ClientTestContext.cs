using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using ClimbOn.Client.Resources;
using ClimbOn.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace ClimbOn.Client.Tests;

// The client's services over a FakeApi, in a chosen UI language. JS calls not set up return defaults. Every text
// a page asks for that the resources do not have is recorded in MissingTexts.
public abstract class ClientTestContext : BunitContext
{
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

    protected ClientTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddSingleton<MissingTextRecorder>();
        Services.AddSingleton<IStringLocalizer<UiTexts>>(services => services.GetRequiredService<MissingTextRecorder>());
        Services.AddSingleton(Api);
        Services.AddScoped(_ => Api.Client());
        Services.AddScoped<ClimberLanguage>();
        UseLanguage(UiLanguage.English);
    }

    protected FakeApi Api { get; } = new();

    protected BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    protected IReadOnlyList<string> MissingTexts => Services.GetRequiredService<MissingTextRecorder>().Missing;

    // The client's own text for a key in the current language, so a test names the key rather than its wording (C45).
    protected string Text(string key) => Services.GetRequiredService<IStringLocalizer<UiTexts>>()[key].Value;

    // As Program does at startup: the culture whose texts the client shows.
    protected static void UseLanguage(string tag)
    {
        var culture = CultureInfo.GetCultureInfo(tag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    protected override void Dispose(bool disposing)
    {
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        base.Dispose(disposing);
    }
}

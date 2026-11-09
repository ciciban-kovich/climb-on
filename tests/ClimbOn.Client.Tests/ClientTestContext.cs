using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using ClimbOn.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClimbOn.Client.Tests;

// The client's services over a FakeApi, in a chosen UI language. JS calls not set up return defaults.
public abstract class ClientTestContext : BunitContext
{
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

    protected ClientTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddSingleton(Api);
        Services.AddScoped(_ => Api.Client());
        Services.AddScoped<ClimberLanguage>();
        UseLanguage(UiLanguage.English);
    }

    protected FakeApi Api { get; } = new();

    protected BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

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

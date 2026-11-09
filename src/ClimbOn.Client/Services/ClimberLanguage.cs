using System.Globalization;
using ClimbOn.Client.Api;
using Microsoft.JSInterop;

namespace ClimbOn.Client.Services;

// C62: the page language follows the logged-in climber's stored language (S104), which the api owns.
public sealed class ClimberLanguage(ClimberApiClient api, IJSRuntime js)
{
    public const string StoredLanguageFunction = "climbOn.storedLanguage";

    public const string BrowserLanguageFunction = "climbOn.browserLanguage";

    public const string StoreLanguageFunction = "climbOn.storeLanguage";

    public const string SetPageLanguageFunction = "climbOn.setPageLanguage";

    // Before the host runs: the culture whose texts the client shows, with the page language set to match. A
    // browser that refuses these calls still gets the app, in the language StartupCulture falls back to.
    public static async Task<CultureInfo> StartupCultureAsync(IJSRuntime js)
    {
        var culture = UiLanguage.StartupCulture(
            await ReadAsync(js, StoredLanguageFunction),
            await ReadAsync(js, BrowserLanguageFunction));
        try
        {
            await js.InvokeVoidAsync(SetPageLanguageFunction, UiLanguage.Of(culture));
        }
        catch (JSException)
        {
            // index.html's own lang stays.
        }

        return culture;
    }

    private static async Task<string?> ReadAsync(IJSRuntime js, string function)
    {
        try
        {
            return await js.InvokeAsync<string?>(function);
        }
        catch (JSException)
        {
            return null;
        }
    }

    // Applies the stored language if a climber is logged in. True if the app must reload to show it.
    public async Task<bool> ApplyStoredAsync(CancellationToken cancellationToken = default)
    {
        string? language;
        try
        {
            language = (await api.Api.Climber.Me.GetAsync(cancellationToken: cancellationToken))?.Language;
        }
        catch (Exception)
        {
            // Not logged in, the api unreachable, or a reply that is not the api's: the startup language stays.
            return false;
        }

        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        await js.InvokeVoidAsync(StoreLanguageFunction, cancellationToken, language);
        await js.InvokeVoidAsync(SetPageLanguageFunction, cancellationToken, language);

        // A reload shows the language only if the browser kept it: startup reads it back. Without storage
        // (blocked, private mode) a reload would start from the browser's language again, and reload again.
        return language != UiLanguage.Of(CultureInfo.CurrentUICulture)
            && await js.InvokeAsync<string?>(StoredLanguageFunction, cancellationToken) == language;
    }
}

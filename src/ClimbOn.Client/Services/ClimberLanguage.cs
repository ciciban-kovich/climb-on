using System.Globalization;
using ClimbOn.Client.Api;
using Microsoft.JSInterop;
using Microsoft.Kiota.Abstractions;

namespace ClimbOn.Client.Services;

// C62: the page language follows the logged-in climber's stored language (S104), which the api owns.
public sealed class ClimberLanguage(ClimberApiClient api, IJSRuntime js)
{
    public const string StoredLanguageFunction = "climbOn.storedLanguage";

    public const string BrowserLanguageFunction = "climbOn.browserLanguage";

    public const string StoreLanguageFunction = "climbOn.storeLanguage";

    public const string SetPageLanguageFunction = "climbOn.setPageLanguage";

    // Applies the stored language if a climber is logged in. True if the app must reload to show it.
    public async Task<bool> ApplyStoredAsync(CancellationToken cancellationToken = default)
    {
        string? language;
        try
        {
            language = (await api.Api.Climber.Me.GetAsync(cancellationToken: cancellationToken))?.Language;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            // Not logged in, or the api is unreachable: the startup language stays.
            return false;
        }

        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        await js.InvokeVoidAsync(StoreLanguageFunction, cancellationToken, language);
        await js.InvokeVoidAsync(SetPageLanguageFunction, cancellationToken, language);
        return language != UiLanguage.Of(CultureInfo.CurrentUICulture);
    }
}

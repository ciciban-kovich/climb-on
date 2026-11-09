using System.Globalization;
using ClimbOn.Client;
using ClimbOn.Client.Api;
using ClimbOn.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization();

// Same origin as the api (design §2), so the browser sends the session cookie with every call.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(services => new ClimberApiClient(new HttpClientRequestAdapter(
    new AnonymousAuthenticationProvider(), httpClient: services.GetRequiredService<HttpClient>())
{
    BaseUrl = builder.HostEnvironment.BaseAddress.TrimEnd('/'),
}));
builder.Services.AddScoped<ClimberLanguage>();

var host = builder.Build();

// Satellite resources load for the culture set before the host runs, so a language change reloads the app.
var js = host.Services.GetRequiredService<IJSRuntime>();
var culture = UiLanguage.StartupCulture(
    await js.InvokeAsync<string?>(ClimberLanguage.StoredLanguageFunction),
    await js.InvokeAsync<string?>(ClimberLanguage.BrowserLanguageFunction));
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
await js.InvokeVoidAsync(ClimberLanguage.SetPageLanguageFunction, UiLanguage.Of(culture));

await host.RunAsync();

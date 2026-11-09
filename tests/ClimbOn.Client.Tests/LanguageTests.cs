using System.Globalization;
using System.Net;
using Bunit;
using ClimbOn.Client.Layout;
using ClimbOn.Client.Pages;
using ClimbOn.Client.Resources;
using ClimbOn.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ClimbOn.Client.Tests;

public sealed class LanguageTests : ClientTestContext
{
    private const string MeRoute = "/api/climber/me";

    public static TheoryData<Type, string> Pages => new()
    {
        { typeof(MainLayout), "nav" },
        { typeof(Home), "h1" },
        { typeof(Register), "details" },
        { typeof(Confirm), "[role=alert]" },
        { typeof(Login), "form" },
        { typeof(NotFound), "h1" },
    };

    // S103: every page renders in both languages, each from the client's own resources: no text it asks for is
    // missing, and the two languages differ. Parity (C46) keeps nb from falling back to English.
    [Theory]
    [Trait("Spec", "S103")]
    [MemberData(nameof(Pages))]
    public void Both_supported_languages_render(Type page, string settledWhen)
    {
        Services.AddSingleton<MissingTextRecorder>();
        Services.AddSingleton<IStringLocalizer<UiTexts>>(services => services.GetRequiredService<MissingTextRecorder>());
        Api.Answer(HttpMethod.Get, "/api/public/legal/terms", HttpStatusCode.OK, LegalDocument("terms"))
            .Answer(HttpMethod.Get, "/api/public/legal/privacy", HttpStatusCode.OK, LegalDocument("privacy"));

        var english = RenderIn(UiLanguage.English, page, settledWhen);
        var bokmal = RenderIn(UiLanguage.Bokmal, page, settledWhen);

        Assert.Empty(Services.GetRequiredService<MissingTextRecorder>().Missing);
        Assert.NotEqual(english, bokmal);
    }

    // C62: once logged in, the stored language is the page's language, and the client's texts follow it.
    [Theory]
    [InlineData(UiLanguage.English, UiLanguage.Bokmal, true)]
    [InlineData(UiLanguage.Bokmal, UiLanguage.English, true)]
    [InlineData(UiLanguage.English, UiLanguage.English, false)]
    [InlineData(UiLanguage.Bokmal, UiLanguage.Bokmal, false)]
    public void Page_lang_follows_stored_language(string shown, string stored, bool reloads)
    {
        UseLanguage(shown);
        Api.Answer(HttpMethod.Get, MeRoute, HttpStatusCode.OK, Me(stored));
        JSInterop.Setup<string?>(ClimberLanguage.StoredLanguageFunction).SetResult(stored);

        var layout = Render<MainLayout>();

        layout.WaitForAssertion(() => Assert.Equal(stored, JSInterop.VerifyInvoke(ClimberLanguage.SetPageLanguageFunction).Arguments.Single()));
        Assert.Equal(stored, JSInterop.VerifyInvoke(ClimberLanguage.StoreLanguageFunction).Arguments.Single());
        layout.WaitForAssertion(() => Assert.Equal(reloads, Navigation.History.Any(entry => entry.Options.ForceLoad)));
    }

    // Storage that keeps nothing (blocked, private mode): a reload would start from the browser's language
    // again and reload again, so the page language follows and the texts stay as they are.
    [Fact]
    public void No_reload_when_the_browser_does_not_keep_the_language()
    {
        UseLanguage(UiLanguage.English);
        Api.Answer(HttpMethod.Get, MeRoute, HttpStatusCode.OK, Me(UiLanguage.Bokmal));
        JSInterop.Setup<string?>(ClimberLanguage.StoredLanguageFunction).SetResult(null);

        var layout = Render<MainLayout>();

        layout.WaitForAssertion(() => JSInterop.VerifyInvoke(ClimberLanguage.StoredLanguageFunction));
        Assert.Equal(UiLanguage.Bokmal, JSInterop.VerifyInvoke(ClimberLanguage.SetPageLanguageFunction).Arguments.Single());
        Assert.Empty(Navigation.History);
    }

    // Not logged in: the language chosen at startup stays.
    [Fact]
    public void Page_lang_stays_without_a_session()
    {
        Api.Fail(HttpMethod.Get, MeRoute, HttpStatusCode.Unauthorized, "session_required", "Server text.");

        Render<MainLayout>();

        JSInterop.VerifyNotInvoke(ClimberLanguage.SetPageLanguageFunction);
        JSInterop.VerifyNotInvoke(ClimberLanguage.StoreLanguageFunction);
        Assert.Empty(Navigation.History);
    }

    [Theory]
    [InlineData("en", UiLanguage.English)]
    [InlineData("en-GB", UiLanguage.English)]
    [InlineData("nb", UiLanguage.Bokmal)]
    [InlineData("nb-NO", UiLanguage.Bokmal)]
    [InlineData("de-DE", UiLanguage.English)]
    public void Shown_language_is_the_resources_a_culture_reaches(string culture, string expected) =>
        Assert.Equal(expected, UiLanguage.Of(CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData("nb", "nb-NO", "nb")]
    [InlineData(null, "nb-NO", "nb-NO")]
    [InlineData(null, "en-GB", "en-GB")]
    [InlineData(null, null, "en")]
    [InlineData("not a language tag !", null, "en")]
    public void Startup_culture_prefers_stored_language(string? stored, string? browser, string expected) =>
        Assert.Equal(expected, UiLanguage.StartupCulture(stored, browser).Name);

    private string RenderIn(string language, Type page, string settledWhen)
    {
        UseLanguage(language);
        var rendered = Render(builder =>
        {
            builder.OpenComponent(0, page);
            builder.CloseComponent();
        });
        rendered.WaitForAssertion(() => Assert.NotEmpty(rendered.FindAll(settledWhen)));
        return rendered.Markup;
    }

    private static object Me(string language) => new { email = "climber@example.no", language, emailConfirmed = true };

    private static object LegalDocument(string document) => new
    {
        document,
        version = 1,
        publishedAt = DateTimeOffset.UnixEpoch,
        language = "en",
        text = "Text.",
    };
}

using System.Net;
using Bunit;
using ClimbOn.Client.Pages;
using ClimbOn.Client.Services;
using Xunit;

namespace ClimbOn.Client.Tests;

// The register, confirm and login pages send what was entered to the api and show what it answers.
// No rule is checked in the client (C7): empty input is sent as it is.
public sealed class AuthPagesTests : ClientTestContext
{
    private const string AccountsRoute = "/api/climber/accounts";
    private const string ConfirmationsRoute = "/api/climber/email-confirmations";
    private const string LoginRoute = "/api/climber/login";
    private const string MeRoute = "/api/climber/me";
    private const string TermsRoute = "/api/public/legal/terms";
    private const string PrivacyRoute = "/api/public/legal/privacy";

    public AuthPagesTests()
    {
        Api.Answer(HttpMethod.Get, TermsRoute, HttpStatusCode.OK, LegalDocument("terms", 3))
            .Answer(HttpMethod.Get, PrivacyRoute, HttpStatusCode.OK, LegalDocument("privacy", 4));
    }

    // The token is read from the fragment and travels in the body only, never in a URL (design §3).
    [Fact]
    public void Confirm_reads_token_from_fragment()
    {
        Api.Answer(HttpMethod.Post, ConfirmationsRoute, HttpStatusCode.NoContent);
        Navigation.NavigateTo("/confirm#t=abc%2Bdef_123");

        var page = Render<Confirm>();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        var request = Api.Single(HttpMethod.Post, ConfirmationsRoute);
        Assert.Equal("abc+def_123", request.StringProperty("token"));
        Assert.Empty(request.Query);
        Assert.Empty(request.Fragment);
    }

    [Fact]
    public void Confirm_without_token_sends_none_and_shows_server_message()
    {
        Api.Fail(HttpMethod.Post, ConfirmationsRoute, HttpStatusCode.BadRequest, "confirmation_token_invalid", "Server text.");
        Navigation.NavigateTo("/confirm");

        var page = Render<Confirm>();

        page.WaitForAssertion(() => Assert.Equal("confirmation_token_invalid", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        Assert.Null(Api.Single(HttpMethod.Post, ConfirmationsRoute).StringProperty("token"));
        Assert.Empty(page.FindAll(".success"));
    }

    [Fact]
    public void Register_posts_what_was_entered()
    {
        Api.Answer(HttpMethod.Post, AccountsRoute, HttpStatusCode.Created, new { language = "nb" });
        JSInterop.Setup<string?>(ClimberLanguage.BrowserLanguageFunction).SetResult("nn-NO");
        var page = Render<Register>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("details").Count));

        page.Find("input[type=email]").Change("climber@example.no");
        page.Find("input[type=password]").Change("a long enough password");
        page.Find("input[type=date]").Change("1990-05-17");
        page.Find("input[type=checkbox]").Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        var request = Api.Single(HttpMethod.Post, AccountsRoute);
        Assert.Equal("climber@example.no", request.StringProperty("email"));
        Assert.Equal("a long enough password", request.StringProperty("password"));
        Assert.Equal("1990-05-17", request.StringProperty("dateOfBirth"));

        // S104: the browser's setting as it is; mapping it is the api's.
        Assert.Equal("nn-NO", request.StringProperty("language"));
        Assert.Equal(3, request.IntProperty("termsVersion"));
        Assert.Equal(4, request.IntProperty("privacyVersion"));
    }

    [Fact]
    public void Register_sends_empty_form_and_shows_server_message()
    {
        Api.Fail(HttpMethod.Post, AccountsRoute, HttpStatusCode.BadRequest, "email_invalid", "Server text.");
        var page = Render<Register>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("details").Count));

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("email_invalid", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        var request = Api.Single(HttpMethod.Post, AccountsRoute);
        Assert.Null(request.StringProperty("email"));
        Assert.Null(request.StringProperty("dateOfBirth"));

        // Not accepted: no versions are named, and the api refuses (S113).
        Assert.Null(request.IntProperty("termsVersion"));
        Assert.Null(request.IntProperty("privacyVersion"));
        Assert.Empty(page.FindAll(".success"));
    }

    [Fact]
    public void Login_posts_credentials_and_goes_home()
    {
        Api.Answer(HttpMethod.Post, LoginRoute, HttpStatusCode.NoContent)
            .Answer(HttpMethod.Get, MeRoute, HttpStatusCode.OK, Me(UiLanguage.English));
        Navigation.NavigateTo("/login");
        var page = Render<Login>();

        page.Find("input[type=email]").Change("climber@example.no");
        page.Find("input[type=password]").Change("a long enough password");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(FakeApi.BaseUrl + "/", Navigation.Uri));
        var request = Api.Single(HttpMethod.Post, LoginRoute);
        Assert.Equal("climber@example.no", request.StringProperty("email"));
        Assert.Equal("a long enough password", request.StringProperty("password"));
        Assert.False(Navigation.History.First().Options.ForceLoad);
    }

    // C62: the client's own texts follow the stored language only after a reload.
    [Fact]
    public void Login_reloads_when_stored_language_differs()
    {
        Api.Answer(HttpMethod.Post, LoginRoute, HttpStatusCode.NoContent)
            .Answer(HttpMethod.Get, MeRoute, HttpStatusCode.OK, Me(UiLanguage.Bokmal));
        JSInterop.Setup<string?>(ClimberLanguage.StoredLanguageFunction).SetResult(UiLanguage.Bokmal);
        var page = Render<Login>();

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(FakeApi.BaseUrl + "/", Navigation.Uri));
        Assert.True(Navigation.History.First().Options.ForceLoad);
    }

    // Storage that keeps nothing (blocked, private mode): a reload would start from the browser's language
    // again, so login goes home without one and the texts stay as they are.
    [Fact]
    public void Login_does_not_reload_when_the_browser_keeps_no_language()
    {
        Api.Answer(HttpMethod.Post, LoginRoute, HttpStatusCode.NoContent)
            .Answer(HttpMethod.Get, MeRoute, HttpStatusCode.OK, Me(UiLanguage.Bokmal));
        JSInterop.Setup<string?>(ClimberLanguage.StoredLanguageFunction).SetResult(null);
        var page = Render<Login>();

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(FakeApi.BaseUrl + "/", Navigation.Uri));
        Assert.Equal(UiLanguage.Bokmal, JSInterop.VerifyInvoke(ClimberLanguage.SetPageLanguageFunction).Arguments.Single());
        Assert.False(Navigation.History.First().Options.ForceLoad);
    }

    [Fact]
    public void Login_shows_server_message_and_stays()
    {
        Api.Fail(HttpMethod.Post, LoginRoute, HttpStatusCode.Unauthorized, "login_failed", "Server text.");
        Navigation.NavigateTo("/login");
        var page = Render<Login>();

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("login_failed", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        Assert.Equal(FakeApi.BaseUrl + "/login", Navigation.Uri);
        Assert.DoesNotContain(Api.Requests, request => request.Path == MeRoute);
    }

    private static object LegalDocument(string document, int version) => new
    {
        document,
        version,
        publishedAt = DateTimeOffset.UnixEpoch,
        language = "en",
        text = $"The {document} text.",
    };

    private static object Me(string language) => new { email = "climber@example.no", language, emailConfirmed = true };
}

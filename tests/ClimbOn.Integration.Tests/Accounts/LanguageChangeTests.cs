using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace ClimbOn.Integration.Tests.Accounts;

// S104's change at any time. S104 stays partial until SSO registration (M2) sets the language too.
public sealed class LanguageChangeTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Route = "/api/climber/language";

    [Theory]
    [Trait("SpecPartial", "S104")]
    [InlineData("nb", "en", "en")]
    [InlineData("en", "nb", "nb")]
    [InlineData("en", "nn-NO", "nb")]
    [InlineData("nb", "de", "en")]
    public async Task Climber_changes_the_stored_language(string registered, string setting, string expected)
    {
        var token = await RegisterAndLogInAsync(registered);

        using var change = await ChangeAsync(token, setting);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(new LanguageResponse(expected), await change.Content.ReadFromJsonAsync<LanguageResponse>(TestContext.Current.CancellationToken));
        using var me = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/me"), token);
        Assert.Equal(expected, (await me.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken))?.Language);
    }

    // S105: the next error is in the new language, whatever Accept-Language says.
    [Theory]
    [Trait("SpecPartial", "S104")]
    [Trait("SpecPartial", "S105")]
    [InlineData("nb", "en")]
    [InlineData("en", "nb")]
    public async Task Next_error_follows_the_new_language(string registered, string changed)
    {
        var token = await RegisterAndLogInAsync(registered);
        using var change = await ChangeAsync(token, changed);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{TestEndpoints.Prefix}/climber/fail");
        request.Headers.AcceptLanguage.ParseAdd(registered);

        using var response = await SendAsync(request, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken))?.Code);
        Assert.Equal(changed, Assert.Single(response.Content.Headers.ContentLanguage));
    }

    [Fact]
    [Trait("SpecPartial", "S104")]
    public async Task Change_leaves_other_accounts_alone()
    {
        var token = await RegisterAndLogInAsync("nb");
        var other = await RegisterAndLogInAsync("nb");

        using var change = await ChangeAsync(token, "en");

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var me = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/me"), other);
        Assert.Equal("nb", (await me.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken))?.Language);
    }

    [Fact]
    [Trait("SpecPartial", "S104")]
    public async Task Only_a_logged_in_climber_changes_a_language()
    {
        using var response = await ChangeAsync(token: null, "en");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ClimberSessions.RequiredCode, (await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken))?.Code);
    }

    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task<string> RegisterAndLogInAsync(string language)
    {
        var account = Factory.Data.Account();
        using var registration = await Client().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, language, TermsVersion: 1, PrivacyVersion: 1),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var login = await Client().PostAsJsonAsync(
            "/api/climber/login", new LoginRequest(account.Email, account.Password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var token = SetCookieHeaderValue.ParseList([.. login.Headers.GetValues(HeaderNames.SetCookie)])
            .Single(cookie => cookie.Name == SessionCookie.Name).Value.Value!;
        Factory.Fixtures.Register("session-token", token);
        return token;
    }

    private Task<HttpResponseMessage> ChangeAsync(string? token, string setting) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(new LanguageChangeRequest(setting)) }, token);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? token)
    {
        using (request)
        {
            if (token is not null)
            {
                request.Headers.Add(HeaderNames.Cookie, $"{SessionCookie.Name}={token}");
            }

            return await Client().SendAsync(request, TestContext.Current.CancellationToken);
        }
    }
}

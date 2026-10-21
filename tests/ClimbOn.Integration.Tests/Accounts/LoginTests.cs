using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace ClimbOn.Integration.Tests.Accounts;

// S78, S82, and the password half of S88 and the climber half of S98 (SSO and the admin API are M2).
// The client handles the cookie by hand, so each test knows exactly which session token a request carries.
public sealed class LoginTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string LoginRoute = "/api/climber/login";
    private const string LogoutRoute = "/api/climber/logout";
    private const string MeRoute = "/api/climber/me";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    [Fact]
    [Trait("Spec", "S78")]
    public async Task Registered_account_logs_in_with_email_and_password()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account, "nb");

        var token = await LogInAsync(account);

        using var response = await SendAsync(HttpMethod.Get, MeRoute, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(new MeResponse(account.Email, "nb", EmailConfirmed: false), me);
    }

    // Each session gives the account that logged in, and /me that account's own data. Asked in the reverse
    // of login order, so neither the first nor the last session stored can stand in for the right one.
    [Fact]
    [Trait("Spec", "S78")]
    public async Task Each_session_belongs_to_the_account_that_logged_in()
    {
        var first = Factory.Data.Account();
        var second = Factory.Data.Account();
        await RegisterAsync(first, "nb");
        await RegisterAsync(second, "en");
        var firstToken = await LogInAsync(first);
        var secondToken = await LogInAsync(second);

        using var secondMe = await SendAsync(HttpMethod.Get, MeRoute, secondToken);
        using var firstMe = await SendAsync(HttpMethod.Get, MeRoute, firstToken);

        Assert.Equal(
            new MeResponse(second.Email, "en", EmailConfirmed: false),
            await secondMe.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken));
        Assert.Equal(
            new MeResponse(first.Email, "nb", EmailConfirmed: false),
            await firstMe.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Spec", "S78")]
    public async Task Address_matches_in_any_case_and_with_surrounding_space()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);

        using var response = await PostLoginAsync($"  {account.Email.ToUpperInvariant()} ", account.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Trait("Spec", "S78")]
    public async Task Wrong_password_is_refused()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);

        using var response = await PostLoginAsync(account.Email, account.Password + "x");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, Login.FailedCode);
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
    }

    [Fact]
    [Trait("Spec", "S78")]
    public async Task Unknown_address_gets_the_wrong_password_response()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);

        using var response = await PostLoginAsync(Factory.Data.Account().Email, account.Password);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, Login.FailedCode);
    }

    [Theory]
    [Trait("SpecPartial", "S88")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other")]
    public async Task Password_account_logs_in_only_with_its_password(string? password)
    {
        var account = Factory.Data.Account();
        var other = Factory.Data.Account();
        await RegisterAsync(account);
        await RegisterAsync(other);

        using var response = await PostLoginAsync(account.Email, password == "other" ? other.Password : password);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, Login.FailedCode);
    }

    // Used just before the 30 days are up, twice, so it outlives 30 days from login; then idle for exactly 30 days.
    [Fact]
    [Trait("Spec", "S82")]
    public async Task Session_expires_30_days_after_last_use()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);
        var justBefore = SessionLifetime - TimeSpan.FromSeconds(1);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            Factory.Clock.Advance(justBefore);
            using var used = await SendAsync(HttpMethod.Get, MeRoute, token);
            Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        }

        Factory.Clock.Advance(SessionLifetime);
        using var expired = await SendAsync(HttpMethod.Get, MeRoute, token);

        await AssertErrorAsync(expired, HttpStatusCode.Unauthorized, ClimberSessions.RequiredCode);
    }

    [Fact]
    [Trait("Spec", "S82")]
    public async Task Logout_ends_the_session()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);

        using var logout = await SendAsync(HttpMethod.Post, LogoutRoute, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await SendAsync(HttpMethod.Get, MeRoute, token);
        await AssertErrorAsync(after, HttpStatusCode.Unauthorized, ClimberSessions.RequiredCode);
    }

    [Fact]
    [Trait("Spec", "S82")]
    public async Task Logout_ends_only_that_session()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var first = await LogInAsync(account);
        var second = await LogInAsync(account);

        using var logout = await SendAsync(HttpMethod.Post, LogoutRoute, first);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var other = await SendAsync(HttpMethod.Get, MeRoute, second);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Theory]
    [Trait("SpecPartial", "S98")]
    [InlineData(null)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Climber_api_refuses_requests_without_a_session(string? token)
    {
        using var response = await SendAsync(HttpMethod.Get, MeRoute, token);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ClimberSessions.RequiredCode);
    }

    // A bearer token is refused even beside a live session, and on the login endpoint too.
    [Theory]
    [Trait("SpecPartial", "S98")]
    [InlineData("GET", MeRoute)]
    [InlineData("POST", LogoutRoute)]
    [InlineData("POST", LoginRoute)]
    public async Task Climber_api_refuses_bearer_tokens(string method, string route)
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);
        using var request = Request(new HttpMethod(method), route, token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJSUzI1NiJ9.e30.c2ln");
        if (route == LoginRoute)
        {
            request.Content = JsonContent.Create(new LoginRequest(account.Email, account.Password));
        }

        using var response = await Client().SendAsync(request, TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ClimberSessions.RequiredCode);
        using var me = await SendAsync(HttpMethod.Get, MeRoute, token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // S105: once logged in, errors are in the stored language, whatever Accept-Language says.
    [Theory]
    [Trait("SpecPartial", "S105")]
    [InlineData("nb", "en", "nb")]
    [InlineData("en", "nb", "en")]
    public async Task Errors_follow_stored_language(string stored, string acceptLanguage, string expected)
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account, stored);
        var token = await LogInAsync(account);
        using var request = Request(HttpMethod.Post, $"{TestEndpoints.Prefix}/climber/fail", token);
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        using var response = await Client().SendAsync(request, TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(expected, Assert.Single(response.Content.Headers.ContentLanguage));
    }

    // S105 on climber requests that need no session, and on those routing answers itself (405, 415): a live
    // session still resolves, so the error is in the stored language and the session is used.
    [Theory]
    [Trait("SpecPartial", "S105")]
    [InlineData("DELETE", MeRoute, null, null, HttpStatusCode.MethodNotAllowed, ErrorCodes.MethodNotAllowed)]
    [InlineData("PUT", "/api/climber/language", "text/plain", """{"language":"en"}""", HttpStatusCode.UnsupportedMediaType, ErrorCodes.UnsupportedMediaType)]
    [InlineData("POST", "/api/climber/email-confirmations", "application/json", """{"token":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""", HttpStatusCode.BadRequest, ConfirmEmail.LinkInvalidCode)]
    public async Task Session_resolves_on_every_climber_request(
        string method, string route, string? contentType, string? body, HttpStatusCode status, string code)
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account, "nb");
        var token = await LogInAsync(account);
        Factory.Clock.Advance(TimeSpan.FromDays(3));
        using var request = Request(new HttpMethod(method), route, token);
        request.Headers.AcceptLanguage.ParseAdd("en");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        using var response = await Client().SendAsync(request, TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, status, code);
        Assert.Equal("nb", Assert.Single(response.Content.Headers.ContentLanguage));
        AssertSessionCookie(SessionCookieOf(response), Factory.Clock.UtcNow + SessionLifetime);
        Assert.Equal(Factory.Clock.UtcNow, await LastActivityAsync(account.Email));
    }

    // Design §2 'Runtime components'. Every authenticated request moves the cookie's expiry with the session's.
    [Fact]
    public async Task Cookie_is_httponly_secure_samesite_strict()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);

        using var login = await PostLoginAsync(account.Email, account.Password);
        var cookie = SessionCookieOf(login);
        AssertSessionCookie(cookie, Factory.Clock.UtcNow + SessionLifetime);

        Factory.Clock.Advance(TimeSpan.FromDays(3));
        using var used = await SendAsync(HttpMethod.Get, MeRoute, cookie.Value.Value);
        AssertSessionCookie(SessionCookieOf(used), Factory.Clock.UtcNow + SessionLifetime);
    }

    // The server extends the session on a failed request too, so the cookie must follow.
    [Fact]
    public async Task Failed_request_still_refreshes_the_cookie()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);
        Factory.Clock.Advance(TimeSpan.FromDays(3));

        using var response = await SendAsync(HttpMethod.Post, $"{TestEndpoints.Prefix}/climber/fail", token);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        AssertSessionCookie(SessionCookieOf(response), Factory.Clock.UtcNow + SessionLifetime);
    }

    // A 401 says the climber is not logged in, so it never hands out a session cookie.
    [Fact]
    public async Task Unauthenticated_response_does_not_refresh_the_cookie()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);

        using var response = await SendAsync(HttpMethod.Post, $"{TestEndpoints.Prefix}/climber/unauthenticated", token);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ClimberSessions.RequiredCode);
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
    }

    // Logout's expired cookie is the only session cookie in its response.
    [Fact]
    public async Task Logout_expires_the_cookie()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);

        using var response = await SendAsync(HttpMethod.Post, LogoutRoute, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(SessionCookieOf(response).Expires < Factory.Clock.UtcNow);
    }

    // Below the API, so no Spec trait (C21.1).
    [Fact]
    public async Task Session_token_is_stored_only_as_its_hash()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);

        var token = await LogInAsync(account);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>();
        var stored = await db.Sessions.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), stored.TokenHash);
    }

    // Below the API, so no Spec trait (C21.1). Logging in counts as activity too.
    [Fact]
    public async Task Authenticated_request_updates_last_activity()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var token = await LogInAsync(account);
        Assert.Equal(Factory.Clock.UtcNow, await LastActivityAsync(account.Email));
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        using var response = await SendAsync(HttpMethod.Get, MeRoute, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Factory.Clock.UtcNow, await LastActivityAsync(account.Email));
    }

    // Below the API, so no Spec trait (C21.1). The endpoint writes last activity itself and rolls back;
    // the pipeline's write had already committed.
    [Fact]
    public async Task Last_activity_survives_failed_request()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        var token = await LogInAsync(account);
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        using var response = await SendAsync(HttpMethod.Post, $"{TestEndpoints.Prefix}/climber/fail", token);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(Factory.Clock.UtcNow, await LastActivityAsync(account.Email));
    }

    [Theory]
    [InlineData(Login.FailedCode)]
    [InlineData(ClimberSessions.RequiredCode)]
    public void Login_and_session_codes_have_a_text_in_both_languages(string code) =>
        Assert.All(new[] { Language.English, Language.Bokmal }, language => Assert.NotEqual(code, Texts.Get(code, language)));

    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task RegisterAsync(AccountDraft account, string language = "nb")
    {
        using var response = await Client().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, language, TermsVersion: 1, PrivacyVersion: 1),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostLoginAsync(string? email, string? password) =>
        Client().PostAsJsonAsync(LoginRoute, new LoginRequest(email, password), TestContext.Current.CancellationToken);

    // The new session's token, registered for the C33 scan.
    private async Task<string> LogInAsync(AccountDraft account)
    {
        using var response = await PostLoginAsync(account.Email, account.Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var token = SessionCookieOf(response).Value.Value!;
        Factory.Fixtures.Register("session-token", token);
        return token;
    }

    private static HttpRequestMessage Request(HttpMethod method, string route, string? token)
    {
        var request = new HttpRequestMessage(method, route);
        if (token is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{SessionCookie.Name}={token}");
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? token)
    {
        using var request = Request(method, route, token);
        return await Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static SetCookieHeaderValue SessionCookieOf(HttpResponseMessage response) =>
        Assert.Single(
            SetCookieHeaderValue.ParseList(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values) ? [.. values] : []),
            cookie => cookie.Name == SessionCookie.Name);

    private static void AssertSessionCookie(SetCookieHeaderValue cookie, DateTimeOffset expires)
    {
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.Null(cookie.Domain.Value);
        Assert.Equal(expires, cookie.Expires);
    }

    private async Task<DateTimeOffset> LastActivityAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>();
        return await db.Accounts.AsNoTracking().Where(account => account.Email == email)
            .Select(account => account.LastActivityAt).SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(code, body?.Code);
    }
}

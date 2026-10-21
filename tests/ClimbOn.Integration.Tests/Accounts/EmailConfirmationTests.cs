using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Email;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Domain.Accounts;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClimbOn.Integration.Tests.Accounts;

// S49's email-registration path; the SSO path (an unverified provider address) is M2.
public sealed class EmailConfirmationTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Route = "/api/climber/email-confirmations";

    [Fact]
    [Trait("SpecPartial", "S49")]
    public async Task Registration_sends_a_confirmation_link_to_the_address()
    {
        var account = Factory.Data.Account();

        await RegisterAsync(account);

        var email = Assert.Single(Factory.Email.Sent);
        Assert.Equal(account.Email, email.To);
        Assert.Contains($"{ApiFactory.PublicBaseUrl}/confirm#t=", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("SpecPartial", "S49")]
    public async Task Link_confirms_the_address()
    {
        await RegisterAsync(Factory.Data.Account());

        using var response = await ConfirmAsync(Token(Assert.Single(Factory.Email.Sent)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S49")]
    public async Task Token_is_single_use()
    {
        await RegisterAsync(Factory.Data.Account());
        var token = Token(Assert.Single(Factory.Email.Sent));
        using var first = await ConfirmAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var second = await ConfirmAsync(token);

        await AssertRejectedAsync(second);
    }

    // Confirmation links have no lifetime (task 10): no requirement gives one, and nothing resends them.
    [Fact]
    [Trait("SpecPartial", "S49")]
    public async Task Token_does_not_expire()
    {
        await RegisterAsync(Factory.Data.Account());
        var token = Token(Assert.Single(Factory.Email.Sent));
        Factory.Clock.Advance(TimeSpan.FromDays(400));

        using var response = await ConfirmAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Task 10: the link is {PublicBaseUrl}/confirm#t=<token>. The token is in the fragment, which browsers
    // never send, and carries 256 random bits.
    [Fact]
    [Trait("SpecPartial", "S49")]
    public async Task Link_carries_token_in_fragment()
    {
        await RegisterAsync(Factory.Data.Account());
        var link = Link(Assert.Single(Factory.Email.Sent));

        Assert.Equal(new Uri(ApiFactory.PublicBaseUrl).Authority, link.Authority);
        Assert.Equal("/confirm", link.AbsolutePath);
        Assert.Empty(link.Query);
        Assert.StartsWith("#t=", link.Fragment, StringComparison.Ordinal);
        Assert.Equal(32, Base64Url.DecodeFromChars(link.Fragment.AsSpan("#t=".Length)).Length);
    }

    [Theory]
    [Trait("SpecPartial", "S49")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Unknown_token_is_rejected(string? token)
    {
        await RegisterAsync(Factory.Data.Account());

        using var response = await ConfirmAsync(token);

        await AssertRejectedAsync(response);
    }

    // S105: the email is in the language stored for the account.
    [Theory]
    [Trait("SpecPartial", "S105")]
    [InlineData("nb", Language.Bokmal)]
    [InlineData("en", Language.English)]
    public async Task Email_is_in_the_account_language(string setting, Language expected)
    {
        await RegisterAsync(Factory.Data.Account(), setting);

        var email = Assert.Single(Factory.Email.Sent);
        Assert.Equal(Texts.Get(EmailContent.ConfirmationSubjectKey, expected), email.Subject);
        var body = string.Format(
            CultureInfo.GetCultureInfo(expected.Tag()), Texts.Get(EmailContent.ConfirmationBodyKey, expected), Link(email).OriginalString);
        Assert.Equal(body, email.Body);
    }

    // Below the API, so no Spec trait (C21.1): the confirmation is recorded on the account.
    [Fact]
    public async Task Confirmation_records_the_time_on_the_account()
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        Assert.Null(await ConfirmedAtAsync(account.Email));
        Factory.Clock.Advance(TimeSpan.FromHours(3));

        using var response = await ConfirmAsync(Token(Assert.Single(Factory.Email.Sent)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Factory.Clock.UtcNow, await ConfirmedAtAsync(account.Email));
    }

    // Below the API, so no Spec trait (C21.1): only the token's SHA-256 hash is stored.
    [Fact]
    public async Task Token_is_stored_only_as_its_hash()
    {
        await RegisterAsync(Factory.Data.Account());
        var token = Token(Assert.Single(Factory.Email.Sent));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>();
        var stored = await db.LinkTokens.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), stored.TokenHash);
    }

    // C46 checks the two files agree; this checks the code is among their keys.
    [Fact]
    public void Link_invalid_code_has_a_text_in_both_languages() =>
        Assert.All(new[] { Language.English, Language.Bokmal }, language =>
            Assert.NotEqual(ConfirmEmail.LinkInvalidCode, Texts.Get(ConfirmEmail.LinkInvalidCode, language)));

    private static Uri Link(EmailMessage email)
    {
        var start = email.Body.IndexOf(ApiFactory.PublicBaseUrl, StringComparison.Ordinal);
        Assert.True(start >= 0, "The email holds no link.");
        var end = email.Body.IndexOfAny([' ', '\r', '\n'], start);
        return new Uri(end < 0 ? email.Body[start..] : email.Body[start..end]);
    }

    private static string Token(EmailMessage email) => Link(email).Fragment["#t=".Length..];

    private async Task RegisterAsync(AccountDraft account, string language = "nb")
    {
        using var response = await Factory.CreateClient().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, language, TermsVersion: 1, PrivacyVersion: 1),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> ConfirmAsync(string? token) =>
        Factory.CreateClient().PostAsJsonAsync(Route, new EmailConfirmationRequest(token), TestContext.Current.CancellationToken);

    private async Task<DateTimeOffset?> ConfirmedAtAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>();
        return await db.Accounts.AsNoTracking().Where(account => account.Email == email)
            .Select(account => account.EmailConfirmedAt).SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(ConfirmEmail.LinkInvalidCode, body?.Code);
    }
}

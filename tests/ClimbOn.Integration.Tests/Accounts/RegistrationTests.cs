using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Domain.Accounts;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Accounts;

public sealed class RegistrationTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Route = "/api/climber/accounts";

    // FixedClock.Start is 2026-01-15, so this is the date of an 18th birthday on the registration date.
    private static readonly DateOnly EighteenToday = new(2008, 1, 15);

    [Fact]
    [Trait("SpecPartial", "S1")]
    public async Task Email_registration_is_accepted()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S27")]
    public async Task Missing_date_of_birth_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { DateOfBirth = null });

        await AssertRejectedAsync(response, Account.DateOfBirthRequiredCode);
    }

    // S27: a date only. A value with a time of day is not a date and does not bind.
    [Theory]
    [Trait("SpecPartial", "S27")]
    [InlineData("2000-01-01T10:00:00")]
    [InlineData("2000-01-01T00:00:00Z")]
    public async Task Date_of_birth_with_a_time_is_rejected(string value)
    {
        var account = Factory.Data.Account();
        var body = JsonSerializer.SerializeToNode(Request(account), JsonSerializerOptions.Web)!;
        body["dateOfBirth"] = value;
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await Factory.CreateClient().PostAsync(Route, content, TestContext.Current.CancellationToken);

        await AssertRejectedAsync(response, ErrorCodes.ValidationFailed);
    }

    [Fact]
    [Trait("SpecPartial", "S28")]
    public async Task Rejected_day_before_18th_birthday()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account(EighteenToday.AddDays(1))));

        await AssertRejectedAsync(response, AgePolicy.UnderageCode);
    }

    [Fact]
    [Trait("SpecPartial", "S28")]
    public async Task Accepted_on_18th_birthday()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account(EighteenToday)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The registration date is the clock's UTC date (C3, C4): at 23:30 UTC on the day before the
    // birthday it is still the day before.
    [Fact]
    [Trait("SpecPartial", "S28")]
    public async Task Registration_date_is_the_utc_date_from_the_clock()
    {
        Factory.Clock.Set(new DateTimeOffset(2026, 1, 14, 23, 30, 0, TimeSpan.Zero));
        using var before = await RegisterAsync(Request(Factory.Data.Account(EighteenToday)));
        await AssertRejectedAsync(before, AgePolicy.UnderageCode);

        Factory.Clock.Set(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        using var after = await RegisterAsync(Request(Factory.Data.Account(EighteenToday)));
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S28")]
    public async Task Date_of_birth_in_the_future_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account(new DateOnly(2026, 1, 16))));

        await AssertRejectedAsync(response, AgePolicy.UnderageCode);
    }

    // C24: shown means returned by the API. Registration returns the stored language and nothing else.
    [Fact]
    [Trait("SpecPartial", "S30")]
    public async Task Registration_response_does_not_return_date_of_birth()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "language" }, body.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [Trait("SpecPartial", "S47")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("climber.example.test")]
    [InlineData("@example.test")]
    [InlineData("climber@")]
    [InlineData("climber@example")]
    [InlineData("climber@example.")]
    [InlineData("climber@@example.test")]
    [InlineData("climber@one@example.test")]
    [InlineData("clim ber@example.test")]
    public async Task Malformed_email_is_rejected(string? email)
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Email = email });

        await AssertRejectedAsync(response, Account.EmailInvalidCode);
    }

    [Fact]
    [Trait("SpecPartial", "S47")]
    public async Task Email_longer_than_254_characters_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Email = Factory.Data.Email(255) });

        await AssertRejectedAsync(response, Account.EmailInvalidCode);
    }

    [Theory]
    [Trait("SpecPartial", "S79")]
    [InlineData(9, PasswordPolicy.TooShortCode)]
    [InlineData(129, PasswordPolicy.TooLongCode)]
    public async Task Password_outside_10_to_128_characters_is_rejected(int length, string code)
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Password = Factory.Data.Password(length) });

        await AssertRejectedAsync(response, code);
    }

    [Theory]
    [Trait("SpecPartial", "S79")]
    [InlineData(10)]
    [InlineData(128)]
    public async Task Password_of_10_to_128_characters_is_accepted(int length)
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Password = Factory.Data.Password(length) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S79")]
    public async Task Missing_password_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Password = null });

        await AssertRejectedAsync(response, PasswordPolicy.TooShortCode);
    }

    // S103: the api answers in either supported language; before an account exists, the request's.
    [Theory]
    [Trait("SpecPartial", "S103")]
    [InlineData("nb-NO", "nb")]
    [InlineData("en-GB", "en")]
    public async Task Refusal_is_in_the_requested_language(string acceptLanguage, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(Request(Factory.Data.Account()) with { DateOfBirth = null }),
        };
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        using var response = await Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        await AssertRejectedAsync(response, Account.DateOfBirthRequiredCode);
        Assert.Equal(new[] { expected }, response.Content.Headers.ContentLanguage);
    }

    [Theory]
    [Trait("SpecPartial", "S104")]
    [InlineData("nb")]
    [InlineData("nn")]
    [InlineData("no")]
    [InlineData("nn-NO")]
    public async Task Nynorsk_and_no_map_to_bokmal(string setting)
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Language = setting });

        Assert.Equal("nb", await StoredLanguageAsync(response));
    }

    [Theory]
    [Trait("SpecPartial", "S104")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData(null)]
    public async Task Other_languages_map_to_english(string? setting)
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { Language = setting });

        Assert.Equal("en", await StoredLanguageAsync(response));
    }

    [Fact]
    [Trait("SpecPartial", "S111")]
    public async Task Missing_terms_acceptance_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { TermsVersion = null });

        await AssertRejectedAsync(response, Account.TermsNotAcceptedCode);
    }

    [Fact]
    [Trait("SpecPartial", "S111")]
    public async Task Missing_privacy_acknowledgement_is_rejected()
    {
        using var response = await RegisterAsync(Request(Factory.Data.Account()) with { PrivacyVersion = null });

        await AssertRejectedAsync(response, Account.PrivacyNotAcknowledgedCode);
    }

    [Fact]
    [Trait("SpecPartial", "S111")]
    public async Task Stale_terms_version_rejected()
    {
        await using var published = await PublishedLegalDocuments.PublishAsync(
            Factory.ConnectionString, "terms", 2, FixedClock.Start, TestContext.Current.CancellationToken);

        using var stale = await RegisterAsync(Request(Factory.Data.Account()) with { TermsVersion = 1 });
        await AssertRejectedAsync(stale, Account.TermsNotAcceptedCode);

        using var current = await RegisterAsync(Request(Factory.Data.Account()) with { TermsVersion = 2 });
        Assert.Equal(HttpStatusCode.Created, current.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S111")]
    public async Task Stale_privacy_version_rejected()
    {
        await using var published = await PublishedLegalDocuments.PublishAsync(
            Factory.ConnectionString, "privacy", 2, FixedClock.Start, TestContext.Current.CancellationToken);

        using var stale = await RegisterAsync(Request(Factory.Data.Account()) with { PrivacyVersion = 1 });
        await AssertRejectedAsync(stale, Account.PrivacyNotAcknowledgedCode);

        using var current = await RegisterAsync(Request(Factory.Data.Account()) with { PrivacyVersion = 2 });
        Assert.Equal(HttpStatusCode.Created, current.StatusCode);
    }

    [Theory]
    [Trait("SpecPartial", "S111")]
    [InlineData(0, 1, Account.TermsNotAcceptedCode)]
    [InlineData(2, 1, Account.TermsNotAcceptedCode)]
    [InlineData(1, 2, Account.PrivacyNotAcknowledgedCode)]
    public async Task Unpublished_version_is_rejected(int terms, int privacy, string code)
    {
        using var response = await RegisterAsync(
            Request(Factory.Data.Account()) with { TermsVersion = terms, PrivacyVersion = privacy });

        await AssertRejectedAsync(response, code);
    }

    // C46 checks the two files agree; this checks the codes are among their keys.
    [Fact]
    public void Every_registration_error_code_has_a_text_in_both_languages() =>
        Assert.Empty(Account.RegistrationCodes.SelectMany(code => new[] { Language.English, Language.Bokmal }
            .Where(language => Texts.Get(code, language) == code)
            .Select(language => $"{code} ({language})")));

    // Version 1 of each document is current unless a test publishes another.
    private static RegistrationRequest Request(AccountDraft account) =>
        new(account.Email, account.Password, account.DateOfBirth, "nb", TermsVersion: 1, PrivacyVersion: 1);

    private Task<HttpResponseMessage> RegisterAsync(RegistrationRequest request) =>
        Factory.CreateClient().PostAsJsonAsync(Route, request, TestContext.Current.CancellationToken);

    private static async Task<string?> StoredLanguageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>(TestContext.Current.CancellationToken);
        return body?.Language;
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(code, body?.Code);
    }
}

using System.Globalization;
using System.Net;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using Xunit;

namespace ClimbOn.Api.Tests;

public sealed class ErrorContractTests(ErrorContractTests.Host host) : IClassFixture<ErrorContractTests.Host>
{
    [Theory]
    [InlineData(DomainErrorKind.Invalid, HttpStatusCode.BadRequest)]
    [InlineData(DomainErrorKind.NotFound, HttpStatusCode.NotFound)]
    [InlineData(DomainErrorKind.Conflict, HttpStatusCode.Conflict)]
    [InlineData(DomainErrorKind.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Domain_error_returns_its_code(DomainErrorKind kind, HttpStatusCode status)
    {
        var (response, body) = await GetAsync($"/domain-error/{kind}");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("test_rule_refused", body?.Code);
        Assert.False(string.IsNullOrWhiteSpace(body?.Message));
    }

    [Theory]
    [Trait("SpecPartial", "S104")]
    [Trait("SpecPartial", "S105")]
    [InlineData("nb")]
    [InlineData("nn")]
    [InlineData("no")]
    [InlineData("nb-NO")]
    [InlineData("nn-NO,en;q=0.8")]
    [InlineData("en;q=0.5, no")]
    public async Task Norwegian_client_setting_gives_bokmal(string acceptLanguage)
    {
        var (response, body) = await GetAsync("/no-such-route", acceptLanguage);

        Assert.Equal(ErrorCodes.NotFound, body?.Code);
        Assert.Equal(["nb"], response.Content.Headers.ContentLanguage);
    }

    [Theory]
    [Trait("SpecPartial", "S104")]
    [Trait("SpecPartial", "S105")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("sv")]
    [InlineData("de-DE,nb;q=0.9")]
    [InlineData("nb;q=0, en")]
    [InlineData("*")]
    [InlineData("not a language;;")]
    [InlineData(null)]
    public async Task Any_other_client_setting_gives_english(string? acceptLanguage)
    {
        var (response, body) = await GetAsync("/no-such-route", acceptLanguage);

        Assert.Equal(ErrorCodes.NotFound, body?.Code);
        Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
    }

    [Theory]
    [Trait("SpecPartial", "S105")]
    [InlineData("nb", "en", "nb")]
    [InlineData("en", "nb", "en")]
    public async Task Stored_account_language_wins_over_client_setting(string stored, string acceptLanguage, string expected)
    {
        using var request = Request("/domain-error/Invalid", acceptLanguage);
        request.Headers.Add(ErrorTestHost.AccountLanguageHeader, stored);

        var (response, body) = await ErrorTestHost.SendAsync(host.Client, request);

        Assert.Equal("test_rule_refused", body?.Code);
        Assert.Equal([expected], response.Content.Headers.ContentLanguage);
    }

    // The one test of translated text (C45): the message is the resource text in the chosen language.
    [Fact]
    public async Task Message_is_translated()
    {
        var (_, english) = await GetAsync("/no-such-route", "en");
        var (_, bokmal) = await GetAsync("/no-such-route", "nb");

        Assert.Equal("The requested resource was not found.", english?.Message);
        Assert.Equal("Fant ikke det du ba om.", bokmal?.Message);
    }

    // Each code has its own text in each language, read without the fallback to English,
    // so this also proves the nb satellite assembly ships with the api.
    [Fact]
    public void Every_api_error_code_has_a_text_in_both_languages()
    {
        var missing = ErrorCodes.All
            .SelectMany(code => new[] { Language.English, Language.Bokmal }.Select(language => (Code: code, Language: language)))
            .Where(pair => string.IsNullOrWhiteSpace(Texts.Resources
                .GetResourceSet(CultureInfo.GetCultureInfo(pair.Language.Tag()), createIfNotExists: true, tryParents: false)
                ?.GetString(pair.Code)))
            .Select(pair => $"{pair.Code} ({pair.Language})");

        Assert.Empty(missing);
    }

    private async Task<(HttpResponseMessage Response, ErrorBody? Body)> GetAsync(string path, string? acceptLanguage = null)
    {
        using var request = Request(path, acceptLanguage);
        return await ErrorTestHost.SendAsync(host.Client, request);
    }

    private static HttpRequestMessage Request(string path, string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ErrorTestHost.Prefix + path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return request;
    }

    public sealed class Host : IDisposable
    {
        private readonly ErrorTestHost factory = new("Production");

        public Host() => Client = factory.CreateClient();

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
        }
    }
}

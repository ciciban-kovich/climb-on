using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Legal;

// Every request here is made without an account: no session exists before login (task 11).
[Trait("SpecPartial", "S112")]
public sealed class LegalDocumentTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task Current_version_is_readable_without_an_account(string document)
    {
        var body = await ReadAsync($"/api/public/legal/{document}");

        Assert.Equal(document, body.Document);
        Assert.Equal(1, body.Version);
        Assert.False(string.IsNullOrWhiteSpace(body.Text));
    }

    [Theory]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task Every_published_version_remains_retrievable(string document)
    {
        await using var published = await PublishedLegalDocuments.PublishAsync(
            Factory.ConnectionString, document, 2, FixedClock.Start, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await ReadAsync($"/api/public/legal/{document}")).Version);
        Assert.Equal(1, (await ReadAsync($"/api/public/legal/{document}/1")).Version);
        Assert.Equal(2, (await ReadAsync($"/api/public/legal/{document}/2")).Version);
    }

    [Theory]
    [InlineData("/api/public/legal/cookies")]
    [InlineData("/api/public/legal/Terms")]
    [InlineData("/api/public/legal/terms/2")]
    [InlineData("/api/public/legal/privacy/0")]
    public async Task Unknown_document_or_version_is_not_found(string path)
    {
        using var response = await Factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.NotFound, body?.Code);
    }

    // S103: the text is published in both supported languages; the request's language picks one.
    [Theory]
    [Trait("SpecPartial", "S103")]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task Text_is_in_the_requested_language(string document)
    {
        var bokmal = await ReadAsync($"/api/public/legal/{document}", "nb-NO", "nb");
        var english = await ReadAsync($"/api/public/legal/{document}", "en", "en");

        Assert.Equal("nb", bokmal.Language);
        Assert.Equal("en", english.Language);
        Assert.NotEqual(english.Text, bokmal.Text);
    }

    private async Task<LegalDocumentResponse> ReadAsync(string path, string? acceptLanguage = null, string? contentLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        using var response = await Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (contentLanguage is not null)
        {
            Assert.Equal(new[] { contentLanguage }, response.Content.Headers.ContentLanguage);
        }

        return (await response.Content.ReadFromJsonAsync<LegalDocumentResponse>(TestContext.Current.CancellationToken))!;
    }
}

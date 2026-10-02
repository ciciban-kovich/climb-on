using ClimbOn.Domain.Accounts;
using Microsoft.AspNetCore.Http;

namespace ClimbOn.Api.Errors;

// Set on the request once the caller is authenticated, carrying the account's stored language (S104).
public sealed record AccountLanguageFeature(Language Language);

public static class MessageLanguage
{
    // S105: the account's stored language; before authentication, the client's setting mapped as in S104.
    public static Language For(HttpContext context) =>
        context.Features.Get<AccountLanguageFeature>()?.Language
            ?? Languages.FromClientSetting(PreferredTag(context.Request));

    // The client's first choice: the Accept-Language entry with the highest quality, ties in header order.
    private static string? PreferredTag(HttpRequest request) =>
        request.GetTypedHeaders().AcceptLanguage
            .Where(entry => entry.Quality is not <= 0)
            .OrderByDescending(entry => entry.Quality ?? 1)
            .Select(entry => entry.Value.Value)
            .FirstOrDefault();
}

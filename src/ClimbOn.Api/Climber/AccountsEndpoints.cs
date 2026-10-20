using ClimbOn.Api.Errors;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Legal;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Legal;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// Language is the client's language setting (S104); a date of birth is a date only, yyyy-MM-dd (S27).
public sealed record RegistrationRequest(
    string? Email,
    string? Password,
    DateOnly? DateOfBirth,
    string? Language,
    int? TermsVersion,
    int? PrivacyVersion);

// The language stored for the account, en or nb.
public sealed record RegistrationResponse(string Language);

// Text is in the request's language (Accept-Language, mapped as in S104).
public sealed record LegalDocumentResponse(string Document, int Version, DateTimeOffset PublishedAt, string Language, string Text);

public static class AccountsEndpoints
{
    private const string Tag = "Accounts";

    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/climber/accounts", RegisterAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .WithTags(Tag);

        // S112: readable without an account, every version retrievable.
        endpoints.MapGet("/api/public/legal/{document}", (string document, HttpContext context, ILegalDocumentStore store, CancellationToken cancellationToken) =>
                ReadLegalDocumentAsync(document, null, context, store, cancellationToken))
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .WithTags(Tag);
        endpoints.MapGet("/api/public/legal/{document}/{version:int}", (string document, int version, HttpContext context, ILegalDocumentStore store, CancellationToken cancellationToken) =>
                ReadLegalDocumentAsync(document, version, context, store, cancellationToken))
            .Produces<ErrorBody>(StatusCodes.Status404NotFound)
            .WithTags(Tag);
        return endpoints;
    }

    private static async Task<Created<RegistrationResponse>> RegisterAsync(
        RegistrationRequest request, Register register, CancellationToken cancellationToken)
    {
        var language = await register.RunAsync(
            new EmailRegistration(
                request.Email, request.Password, request.DateOfBirth, request.Language, request.TermsVersion, request.PrivacyVersion),
            cancellationToken);
        return TypedResults.Created((string?)null, new RegistrationResponse(language.Tag()));
    }

    private static async Task<Results<Ok<LegalDocumentResponse>, NotFound>> ReadLegalDocumentAsync(
        string name, int? version, HttpContext context, ILegalDocumentStore store, CancellationToken cancellationToken)
    {
        if (LegalDocuments.FromName(name) is not { } document
            || await store.FindAsync(document, version, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var language = MessageLanguage.For(context);
        context.Response.Headers.ContentLanguage = language.Tag();
        return TypedResults.Ok(new LegalDocumentResponse(
            found.Document.Name(),
            found.Version,
            found.PublishedAt,
            language.Tag(),
            language == Language.Bokmal ? found.TextBokmal : found.TextEnglish));
    }
}

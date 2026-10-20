using ClimbOn.Api.Errors;
using ClimbOn.Application.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClimbOn.Api.Climber;

// The token from the link's URL fragment (S49); it travels in the body, never in a URL.
public sealed record EmailConfirmationRequest(string? Token);

public static class ConfirmationEndpoints
{
    private const string Tag = "Accounts";

    public static IEndpointRouteBuilder MapConfirmationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/climber/email-confirmations", ConfirmAsync)
            .Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .WithTags(Tag);
        return endpoints;
    }

    private static async Task<NoContent> ConfirmAsync(
        EmailConfirmationRequest request, ConfirmEmail confirm, CancellationToken cancellationToken)
    {
        await confirm.RunAsync(request.Token, cancellationToken);
        return TypedResults.NoContent();
    }
}

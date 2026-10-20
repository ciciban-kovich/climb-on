using ClimbOn.Api.Errors;
using Microsoft.OpenApi;

namespace ClimbOn.Api.OpenApi;

// One OpenAPI document per API (C47); the admin document joins in M2.
public static class ApiDocuments
{
    public const string Climber = "climber";

    public const string ClimberPathPrefix = "api/climber/";

    // Readable without an account (S112); the climber client reads them too.
    public const string PublicPathPrefix = "api/public/";

    // Every 4xx and 5xx leaves the api as { code, message } (C45), so every operation declares it
    // and the generated client reads the code whatever the status.
    private static readonly string[] ErrorStatusRanges = ["4XX", "5XX"];

    public static IServiceCollection AddApiDocuments(this IServiceCollection services)
    {
        services.AddOpenApi(Climber, options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith(ClimberPathPrefix, StringComparison.Ordinal) == true
                || description.RelativePath?.StartsWith(PublicPathPrefix, StringComparison.Ordinal) == true;

            options.AddOperationTransformer(async (operation, context, cancellationToken) =>
            {
                var schema = await context.GetOrCreateSchemaAsync(typeof(ErrorBody), null, cancellationToken);
                context.Document!.AddComponent(nameof(ErrorBody), schema);
                operation.Responses ??= [];
                foreach (var range in ErrorStatusRanges)
                {
                    operation.Responses.TryAdd(range, new OpenApiResponse
                    {
                        Description = "Error",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new() { Schema = new OpenApiSchemaReference(nameof(ErrorBody), context.Document) },
                        },
                    });
                }
            });

            // A served document would name the requesting host; the client sets its own base URL.
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Servers = [];
                return Task.CompletedTask;
            });
        });
        return services;
    }
}

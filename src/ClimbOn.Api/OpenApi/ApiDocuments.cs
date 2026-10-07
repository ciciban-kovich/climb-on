namespace ClimbOn.Api.OpenApi;

// One OpenAPI document per API (C47); the admin document joins in M2.
public static class ApiDocuments
{
    public const string Climber = "climber";

    public const string ClimberPathPrefix = "api/climber/";

    public static IServiceCollection AddApiDocuments(this IServiceCollection services)
    {
        services.AddOpenApi(Climber, options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith(ClimberPathPrefix, StringComparison.Ordinal) == true;

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

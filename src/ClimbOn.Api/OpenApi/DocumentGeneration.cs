using System.Reflection;

namespace ClimbOn.Api.OpenApi;

// Build-time document generation starts Program under dotnet-getdocument with no database or
// Key Vault; the host then runs in this environment, and startup work that needs either is
// guarded with IsEnvironment(EnvironmentName).
public static class DocumentGeneration
{
    public const string EnvironmentName = "ApiDocumentGeneration";

    public static bool IsRunning => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}

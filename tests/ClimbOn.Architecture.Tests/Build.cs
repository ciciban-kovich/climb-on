using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ClimbOn.Architecture.Tests;

// Runs the dotnet CLI outside the test host's MSBuild environment.
internal static class Build
{
    public static readonly string RepoRoot = FindRepoRoot();

    public static readonly string DomainProject =
        Path.Combine(RepoRoot, "src", "ClimbOn.Domain", "ClimbOn.Domain.csproj");

    public static (int ExitCode, string Output) Dotnet(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var name in start.Environment.Keys.Where(IsHostVariable).ToList())
        {
            start.Environment.Remove(name);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    // Evaluation only: no target runs, so items added by targets (package analyzers) do not appear.
    public static JsonElement Evaluate(string project, params string[] switches)
    {
        var (exitCode, output) = Dotnet(Path.GetDirectoryName(project)!, ["msbuild", project, .. switches]);
        Assert.True(exitCode == 0, $"dotnet msbuild {project} failed:\n{output}");
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    public static IEnumerable<JsonElement> Items(JsonElement evaluation, string itemType) =>
        evaluation.GetProperty("Items").GetProperty(itemType).EnumerateArray();

    public static string Metadata(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static bool IsHostVariable(string name) =>
        name.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("MSBUILD", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClimbOn.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("ClimbOn.slnx not found");
    }
}

// A throwaway directory outside the repo. The empty Directory.Build files stop MSBuild from
// importing anything above it.
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "climbon-arch-" + Guid.NewGuid().ToString("N"));

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
        Write("Directory.Build.props", "<Project />");
        Write("Directory.Build.targets", "<Project />");
    }

    public string Write(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A build node that has not exited yet may still hold a file; the OS temp cleanup takes it.
        }
    }
}

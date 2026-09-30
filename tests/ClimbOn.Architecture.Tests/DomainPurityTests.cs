using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ClimbOn.Architecture.Tests;

// C19 at source level. Each check takes paths, runs against the real projects in its own test,
// and runs against a temp project that violates it in Rules_detect_violations.
public sealed class DomainPurityTests
{
    private const string BannedApiAnalyzers = "Microsoft.CodeAnalysis.BannedApiAnalyzers";

    private static readonly string DomainDirectory = Path.GetDirectoryName(Build.DomainProject)!;

    private static readonly string ApplicationProject =
        Path.Combine(Build.RepoRoot, "src", "ClimbOn.Application", "ClimbOn.Application.csproj");

    private static readonly string BannedSymbolsFile = Path.Combine(DomainDirectory, "BannedSymbols.txt");

    private static readonly string[] ImplicitAnalyzers =
        ["Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll", "Microsoft.CodeAnalysis.NetAnalyzers.dll"];

    private static readonly string[] FixedBans =
    [
        "N:System.Net", "N:System.Data",
        "T:System.IO.Path", "N:System.IO.Enumeration", "N:System.IO.MemoryMappedFiles", "N:System.IO.Pipes",
        "N:System.IO.IsolatedStorage",
        "P:System.DateTime.Now", "P:System.DateTime.UtcNow", "P:System.DateTime.Today",
        "P:System.DateTimeOffset.Now", "P:System.DateTimeOffset.UtcNow",
        "P:System.Environment.TickCount", "P:System.Environment.TickCount64",
        "T:System.TimeProvider", "T:System.Diagnostics.Stopwatch", "T:System.Threading.Timer",
        "T:System.Threading.PeriodicTimer", "N:System.Timers",
        "M:System.Guid.CreateVersion7",
    ];

    private static readonly Lazy<string> ReferencePack = new(FindReferencePack);

    [Fact]
    public void Domain_depends_only_on_BCL()
    {
        Assert.Empty(ReferenceViolations(Build.DomainProject));
        Assert.Empty(AssemblyReferenceViolations(Path.Combine(AppContext.BaseDirectory, "ClimbOn.Domain.dll")));
    }

    [Fact]
    public void Domain_never_reads_system_clock()
    {
        Assert.Empty(BannedSymbolsViolations(BannedSymbolsFile));
        Assert.Empty(SuppressionViolations(Build.RepoRoot, DomainDirectory));
    }

    [Fact]
    public void Application_does_not_reference_outer_layers() =>
        Assert.Empty(OuterLayerViolations(ApplicationProject));

    public static TheoryData<string> Checks => ["Canary", "References", "Suppression", "AssemblyReferences", "OuterLayers"];

    [Theory]
    [MemberData(nameof(Checks))]
    public void Rules_detect_violations(string check)
    {
        switch (check)
        {
            case "Canary":
                CanaryReportsEveryBan();
                break;
            case "References":
                ReferencesReportPackageReference();
                break;
            case "Suppression":
                SuppressionReportsEditorconfigSeverity();
                break;
            case "AssemblyReferences":
                AssemblyReferencesReportSystemNetHttp();
                break;
            default:
                OuterLayersReportTransitiveInfrastructure();
                break;
        }
    }

    // Part 1: evaluated items, so anything Directory.Build.* injects counts, plus the lock file.
    private static List<string> ReferenceViolations(string project)
    {
        var evaluation = Build.Evaluate(project,
            "-getItem:ProjectReference;PackageReference;Reference;FrameworkReference;Analyzer");
        var violations = new List<string>();

        foreach (var itemType in new[] { "ProjectReference", "Reference" })
        {
            violations.AddRange(Build.Items(evaluation, itemType).Select(item => $"{itemType} {Build.Metadata(item, "Identity")}"));
        }

        violations.AddRange(Build.Items(evaluation, "PackageReference")
            .Where(item => !(Build.Metadata(item, "Identity") == BannedApiAnalyzers
                && Build.Metadata(item, "PrivateAssets").Equals("all", StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"PackageReference {Build.Metadata(item, "Identity")}"));

        violations.AddRange(Build.Items(evaluation, "FrameworkReference")
            .Where(item => !(Build.Metadata(item, "Identity") == "Microsoft.NETCore.App"
                && Build.Metadata(item, "IsImplicitlyDefined") == "true"))
            .Select(item => $"FrameworkReference {Build.Metadata(item, "Identity")}"));

        violations.AddRange(Build.Items(evaluation, "Analyzer")
            .Where(item => !ImplicitAnalyzers.Contains(Path.GetFileName(Build.Metadata(item, "Identity"))))
            .Select(item => $"Analyzer {Build.Metadata(item, "Identity")}"));

        var lockFile = Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");
        if (!File.Exists(lockFile))
        {
            violations.Add("packages.lock.json missing");
            return violations;
        }

        using var lockJson = JsonDocument.Parse(File.ReadAllText(lockFile));
        var dependencies = lockJson.RootElement.GetProperty("dependencies").EnumerateObject()
            .SelectMany(framework => framework.Value.EnumerateObject())
            .Select(dependency => $"{dependency.Name} ({dependency.Value.GetProperty("type").GetString()})")
            .ToList();
        if (!dependencies.SequenceEqual([$"{BannedApiAnalyzers} (Direct)"]))
        {
            violations.Add("packages.lock.json lists " + string.Join(", ", dependencies));
        }

        return violations;
    }

    // Part 2: entries that match no symbol are ignored by the analyzer, so every overload and
    // every File*/Directory*/Drive* type in the reference pack needs its own line.
    private static List<string> BannedSymbolsViolations(string bannedSymbolsFile)
    {
        if (!File.Exists(bannedSymbolsFile))
        {
            return [$"{bannedSymbolsFile} missing"];
        }

        var entries = BannedEntries(bannedSymbolsFile);
        return FixedBans.Concat(ReferencePackBans())
            .Where(required => !entries.Contains(required))
            .Select(required => "not banned: " + required)
            .ToList();
    }

    private static HashSet<string> BannedEntries(string bannedSymbolsFile) =>
        File.ReadAllLines(bannedSymbolsFile)
            .Select(line => line.Split(';')[0].Trim())
            .Where(entry => entry.Length > 0 && !entry.StartsWith("//", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static SortedSet<string> ReferencePackBans()
    {
        var bans = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(ReferencePack.Value, "*.dll"))
        {
            using var pe = new PEReader(File.OpenRead(file));
            if (!pe.HasMetadata)
            {
                continue;
            }

            var reader = pe.GetMetadataReader();
            foreach (var type in reader.TypeDefinitions.Select(reader.GetTypeDefinition))
            {
                if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                {
                    continue;
                }

                var ns = reader.GetString(type.Namespace);
                var name = reader.GetString(type.Name);
                if (ns == "System.IO" && Regex.IsMatch(name, "^(File|Directory|Drive)"))
                {
                    bans.Add($"T:System.IO.{name}");
                }

                if ((ns, name) is ("System.Threading.Tasks", "Task") or ("System.Threading", "Thread"))
                {
                    var methodName = name == "Task" ? "Delay" : "Sleep";
                    foreach (var method in type.GetMethods().Select(reader.GetMethodDefinition))
                    {
                        if (reader.GetString(method.Name) == methodName
                            && (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
                        {
                            var parameters = method.DecodeSignature(DocIdTypes.Instance, null).ParameterTypes;
                            bans.Add($"M:{ns}.{name}.{methodName}({string.Join(",", parameters)})");
                        }
                    }
                }
            }
        }

        return bans;
    }

    // Part 3.
    private static List<string> SuppressionViolations(string root, string domainDirectory)
    {
        var violations = new List<string>();
        var domainSources = SourceFiles(domainDirectory, "*.cs").ToList();

        foreach (var source in domainSources)
        {
            var text = File.ReadAllText(source);
            if (Regex.IsMatch(text, @"#\s*pragma\s+warning\s+disable") || text.Contains("SuppressMessage", StringComparison.Ordinal))
            {
                violations.Add($"suppression in {source}");
            }
        }

        var domainPaths = domainSources.Append(Path.Combine(domainDirectory, "Probe.cs")).ToList();
        var configs = SourceFiles(root, ".editorconfig").Concat(SourceFiles(root, ".globalconfig"));
        foreach (var config in configs)
        {
            var configDirectory = Path.GetDirectoryName(config)!;
            var sectionMatchesDomain = false;
            foreach (var rawLine in File.ReadAllLines(config))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var glob = GlobRegex(line[1..^1]);
                    sectionMatchesDomain = domainPaths.Any(path => glob.IsMatch(RelativePath(configDirectory, path)));
                    continue;
                }

                var key = line.Split('=')[0].Trim().ToLowerInvariant();
                if (key is "dotnet_diagnostic.rs0030.severity"
                        or "dotnet_analyzer_diagnostic.category-apidesign.severity"
                        or "dotnet_analyzer_diagnostic.severity"
                    || (sectionMatchesDomain && key is "generated_code" or "is_global"))
                {
                    violations.Add($"{config}: {line}");
                }
            }
        }

        return violations;
    }

    // Part 5: the reference table only, never method bodies.
    private static List<string> AssemblyReferenceViolations(string assemblyPath)
    {
        var pack = Directory.EnumerateFiles(ReferencePack.Value, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .Where(name => !pack.Contains(name)
                || name.StartsWith("System.Net", StringComparison.Ordinal)
                || name.StartsWith("System.Data", StringComparison.Ordinal))
            .Select(name => "references " + name)
            .ToList();
    }

    // Application check, over the transitive ProjectReference closure.
    private static List<string> OuterLayerViolations(string project)
    {
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(project) };
        var pending = new Queue<string>(seen);
        while (pending.TryDequeue(out var current))
        {
            foreach (var reference in Build.Items(Build.Evaluate(current, "-getItem:ProjectReference"), "ProjectReference"))
            {
                var path = Path.GetFullPath(Build.Metadata(reference, "FullPath"));
                if (Path.GetFileName(path) is "ClimbOn.Infrastructure.csproj" or "ClimbOn.Api.csproj")
                {
                    violations.Add($"{current} references {path}");
                }
                else if (seen.Add(path))
                {
                    pending.Enqueue(path);
                }
            }
        }

        return violations;
    }

    // Part 4: the real Domain project, built once with the fixture added from a temp directory.
    private static void CanaryReportsEveryBan()
    {
        using var temp = new TempDirectory();
        var fixture = temp.Write("BannedUses.cs",
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Canary", "BannedUses.cs.txt")));
        var targets = temp.Write("canary.targets",
            $"""<Project><ItemGroup><Compile Include="{fixture}" /></ItemGroup></Project>""");

        var (exitCode, output) = Build.Dotnet(DomainDirectory, "build", Build.DomainProject, "-c", "Release",
            "--no-restore", "-nodeReuse:false", "-clp:NoSummary",
            $"-p:CustomAfterMicrosoftCommonTargets={targets}",
            $"-p:OutDir={Path.Combine(temp.Path, "bin")}{Path.DirectorySeparatorChar}",
            $"-p:IntermediateOutputPath={Path.Combine(temp.Path, "obj")}{Path.DirectorySeparatorChar}");

        var lines = File.ReadAllLines(fixture);
        var marked = Enumerable.Range(1, lines.Length)
            .Where(number => lines[number - 1].Contains("// ban: ", StringComparison.Ordinal))
            .ToList();
        // Every diagnostic, including those with no file position ("CSC : warning ...").
        var diagnostics = Regex.Matches(output, @"^\s*(?<origin>.+?) ?: (?<severity>error|warning) (?<code>\w+) ?:", RegexOptions.Multiline)
            .Select(match =>
            {
                var position = Regex.Match(match.Groups["origin"].Value, @"^(?<file>.+)\((?<line>\d+),\d+\)$");
                return (File: position.Success ? position.Groups["file"].Value : match.Groups["origin"].Value,
                    Line: position.Success ? int.Parse(position.Groups["line"].Value) : 0,
                    Severity: match.Groups["severity"].Value, Code: match.Groups["code"].Value);
            })
            .Distinct()
            .ToList();

        Assert.True(exitCode != 0, "the canary build succeeded:\n" + output);
        Assert.All(diagnostics, diagnostic => Assert.True(
            diagnostic is { Severity: "error", Code: "RS0030", Line: > 0 } && PathsEqual(diagnostic.File, fixture),
            $"unexpected diagnostic {diagnostic}\n{output}"));
        Assert.Equal(marked, diagnostics.Select(diagnostic => diagnostic.Line).Distinct().Order());

        var used = lines.Where(line => line.Contains("// ban: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("// ban: ", StringComparison.Ordinal) + 8)..].Trim())
            .ToHashSet(StringComparer.Ordinal);
        var unused = BannedEntries(BannedSymbolsFile).Except(used).Order(StringComparer.Ordinal).ToList();
        Assert.Empty(unused);
    }

    private static void ReferencesReportPackageReference()
    {
        using var temp = new TempDirectory();
        var project = temp.Write("Temp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.4" /></ItemGroup>
            </Project>
            """);

        Assert.Contains("PackageReference Newtonsoft.Json", ReferenceViolations(project));
    }

    private static void SuppressionReportsEditorconfigSeverity()
    {
        using var temp = new TempDirectory();
        var config = temp.Write(".editorconfig", "root = true\n\n[*.cs]\ndotnet_diagnostic.RS0030.severity = none\n");
        var domain = Path.Combine(temp.Path, "src", "ClimbOn.Domain");
        Directory.CreateDirectory(domain);

        Assert.Contains($"{config}: dotnet_diagnostic.RS0030.severity = none", SuppressionViolations(temp.Path, domain));
    }

    private static void AssemblyReferencesReportSystemNetHttp()
    {
        using var temp = new TempDirectory();
        temp.Write("Temp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        temp.Write("Uses.cs", "public static class Uses { public static System.Net.Http.HttpClient Client() => new(); }\n");
        var output = Path.Combine(temp.Path, "out");

        var (exitCode, log) = Build.Dotnet(temp.Path, "build", "Temp.csproj", "-c", "Release", "-nodeReuse:false",
            $"-p:OutDir={output}{Path.DirectorySeparatorChar}");
        Assert.True(exitCode == 0, log);

        Assert.Contains("references System.Net.Http", AssemblyReferenceViolations(Path.Combine(output, "Temp.dll")));
    }

    private static void OuterLayersReportTransitiveInfrastructure()
    {
        using var temp = new TempDirectory();
        var app = temp.Write("App/App.csproj", ProjectReferencing(@"..\X\X.csproj"));
        var x = temp.Write("X/X.csproj", ProjectReferencing(@"..\Infrastructure\ClimbOn.Infrastructure.csproj"));
        var infrastructure = Path.Combine(temp.Path, "Infrastructure", "ClimbOn.Infrastructure.csproj");

        Assert.Contains($"{Path.GetFullPath(x)} references {infrastructure}", OuterLayerViolations(app));
    }

    private static string ProjectReferencing(string path) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup><ProjectReference Include="{path}" /></ItemGroup>
        </Project>
        """;

    private static string FindReferencePack()
    {
        var properties = Build.Evaluate(Build.DomainProject,
            "-getProperty:NetCoreTargetingPackRoot", "-getProperty:BundledNETCoreAppPackageVersion").GetProperty("Properties");
        var pack = Path.Combine(properties.GetProperty("NetCoreTargetingPackRoot").GetString()!,
            "Microsoft.NETCore.App.Ref", properties.GetProperty("BundledNETCoreAppPackageVersion").GetString()!,
            "ref", "net10.0");
        Assert.True(Directory.Exists(pack), $"reference pack not found at {pack}");
        return pack;
    }

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(file => !RelativePath(root, file).Split('/').Any(part => part is "bin" or "obj" or ".git"));

    private static string RelativePath(string from, string to) => Path.GetRelativePath(from, to).Replace('\\', '/');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Editorconfig globs: without a '/', a glob matches the file name in any directory below the file.
    private static Regex GlobRegex(string glob)
    {
        if (!glob.Contains('/'))
        {
            glob = "**/" + glob;
        }

        var pattern = Regex.Escape(glob.TrimStart('/'))
            .Replace(@"\*\*/", "(.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        pattern = Regex.Replace(pattern, @"\\\{(?<alternatives>[^}]*)}",
            match => "(" + match.Groups["alternatives"].Value.Replace(",", "|") + ")");
        return new Regex($"^{pattern}$");
    }

    // Parameter types in documentation-comment ID form, enough for Task.Delay and Thread.Sleep.
    private sealed class DocIdTypes : ISignatureTypeProvider<string, object?>
    {
        public static readonly DocIdTypes Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            throw new NotSupportedException();

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => throw new NotSupportedException();

        public string GetByReferenceType(string elementType) => elementType + "@";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            throw new NotSupportedException();

        public string GetGenericMethodParameter(object? genericContext, int index) => "``" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "`" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => throw new NotSupportedException();

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}

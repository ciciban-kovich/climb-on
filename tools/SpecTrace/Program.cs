using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SpecTrace;

public static class Program
{
    public static int Main(string[] args) =>
        SpecTraceCli.Run(args, Directory.GetCurrentDirectory(), Console.Out, Console.Error, DotnetBuild.Run);
}

/// <summary>Exit codes: 0 pass, 1 rule failure, 2 usage or IO error.</summary>
public static class ExitCode
{
    public const int Pass = 0;
    public const int RuleFailure = 1;
    public const int Error = 2;
}

public sealed class SpecTraceException(string message) : Exception(message);

/// <summary>Builds the solution at <c>root</c>; returns the build's exit code.</summary>
public delegate int SolutionBuilder(string root, string solutionFile, TextWriter log);

public static class SpecTraceCli
{
    private const string Usage = """
        usage: SpecTrace check [--no-build]
               SpecTrace require [--whole] [--no-build] <S-ID>...
        """;

    public static int Run(string[] args, string root, TextWriter stdout, TextWriter stderr, SolutionBuilder build)
    {
        try
        {
            return Dispatch(args, root, stdout, stderr, build);
        }
        catch (SpecTraceException e)
        {
            stderr.WriteLine($"error: {e.Message}");
            return ExitCode.Error;
        }
        catch (Exception e)
        {
            stderr.WriteLine($"error: {e}");
            return ExitCode.Error;
        }
    }

    private static int Dispatch(string[] args, string root, TextWriter stdout, TextWriter stderr, SolutionBuilder build)
    {
        if (args.Length == 0)
        {
            return UsageError(stderr, "no command");
        }

        var command = args[0];
        var noBuild = false;
        var whole = false;
        var ids = new List<string>();
        foreach (var arg in args.Skip(1))
        {
            switch (arg)
            {
                case "--no-build":
                    noBuild = true;
                    break;
                case "--whole" when command == "require":
                    whole = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        return UsageError(stderr, $"unknown option '{arg}'");
                    }

                    ids.Add(arg);
                    break;
            }
        }

        switch (command)
        {
            case "check" when ids.Count == 0:
                return Check(root, noBuild, stdout, stderr, build);
            case "check":
                return UsageError(stderr, "check takes no IDs");
            case "require" when ids.Count > 0:
                return Require(root, ids, whole, noBuild, stdout, stderr, build);
            case "require":
                return UsageError(stderr, "require needs at least one S-ID");
            default:
                return UsageError(stderr, $"unknown command '{command}'");
        }
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"error: {message}");
        stderr.WriteLine(Usage);
        return ExitCode.Error;
    }

    private static int Check(string root, bool noBuild, TextWriter stdout, TextWriter stderr, SolutionBuilder build)
    {
        var specs = SpecIds.Parse(ReadRequired(root, SpecIds.SpecsPath));
        var pending = PendingList.Parse(ReadRequired(root, PendingList.Path), PendingList.Path);
        var baseline = GitBaseline.Read(root, "origin/main");
        var refs = TraitReader.Read(root, noBuild, stderr, build);

        var failures = new List<string>();

        var unknownPending = pending.Where(id => !specs.Contains(id)).ToList();
        if (unknownPending.Count > 0)
        {
            failures.Add($"{PendingList.Path} lists IDs that are not live in {SpecIds.SpecsPath}: {Join(unknownPending)}");
        }

        var unknownRefs = refs.All.Where(id => !specs.Contains(id)).Order(SpecIds.Comparer).ToList();
        if (unknownRefs.Count > 0)
        {
            failures.Add($"tests reference IDs that are not live in {SpecIds.SpecsPath}: {Join(unknownRefs)}");
        }

        var uncovered = specs.Where(id => !refs.Whole.Contains(id) && !pending.Contains(id)).ToList();
        if (uncovered.Count > 0)
        {
            failures.Add($"IDs with no Spec test and not pending (C17): {Join(uncovered)}");
        }

        var pendingWithTest = pending.Where(refs.Whole.Contains).ToList();
        if (pendingWithTest.Count > 0)
        {
            failures.Add($"pending IDs that have a Spec test; remove them from {PendingList.Path} (C68): {Join(pendingWithTest)}");
        }

        var allowedAdditions = baseline.Pending is null
            ? specs.ToHashSet()
            : specs.Where(id => !baseline.Specs.Contains(id)).ToHashSet();
        var added = pending.Where(id => baseline.Pending?.Contains(id) != true && !allowedAdditions.Contains(id)).ToList();
        if (added.Count > 0)
        {
            failures.Add($"IDs added to {PendingList.Path} that are not new in {SpecIds.SpecsPath} since origin/main (C68): {Join(added)}");
        }

        var partialOnly = refs.Partial.Where(id => !refs.Whole.Contains(id)).Order(SpecIds.Comparer).ToList();
        stdout.WriteLine(partialOnly.Count > 0
            ? $"IDs with only SpecPartial tests: {Join(partialOnly)}"
            : "IDs with only SpecPartial tests: none");

        foreach (var failure in failures)
        {
            stdout.WriteLine($"FAIL: {failure}");
        }

        if (failures.Count > 0)
        {
            return ExitCode.RuleFailure;
        }

        stdout.WriteLine($"PASS: {specs.Count} live IDs, {specs.Count(refs.Whole.Contains)} with a Spec test, {pending.Count} pending.");
        return ExitCode.Pass;
    }

    private static int Require(
        string root, List<string> ids, bool whole, bool noBuild, TextWriter stdout, TextWriter stderr, SolutionBuilder build)
    {
        var specs = SpecIds.Parse(ReadRequired(root, SpecIds.SpecsPath));
        var unknown = ids.Where(id => !specs.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new SpecTraceException($"not a live ID in {SpecIds.SpecsPath}: {Join(unknown)}");
        }

        var pending = whole
            ? PendingList.Parse(ReadRequired(root, PendingList.Path), PendingList.Path)
            : [];
        var refs = TraitReader.Read(root, noBuild, stderr, build);

        var failed = false;
        foreach (var id in ids)
        {
            var problem = whole switch
            {
                true when !refs.Whole.Contains(id) => "no Spec test",
                true when pending.Contains(id) => $"still listed in {PendingList.Path}",
                false when !refs.Whole.Contains(id) && !refs.Partial.Contains(id) => "no Spec or SpecPartial test",
                _ => null,
            };
            stdout.WriteLine(problem is null ? $"ok   {id}" : $"FAIL {id}: {problem}");
            failed |= problem is not null;
        }

        return failed ? ExitCode.RuleFailure : ExitCode.Pass;
    }

    private static string ReadRequired(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            throw new SpecTraceException($"{relativePath} not found under {root}");
        }

        return File.ReadAllText(path);
    }

    private static string Join(IEnumerable<string> ids) => string.Join(", ", ids);
}

/// <summary>Live (non-retired) S-IDs of a specs.md, in document order.</summary>
public static partial class SpecIds
{
    public const string SpecsPath = "docs/specs.md";

    public static readonly IComparer<string> Comparer = Comparer<string>.Create((a, b) =>
    {
        var (a1, a2) = Key(a);
        var (b1, b2) = Key(b);
        return a1 != b1 ? a1.CompareTo(b1) : a2.CompareTo(b2);
    });

    [GeneratedRegex(@"^- (S\d+)\.(?:\s|$)")]
    private static partial Regex TopLevel();

    [GeneratedRegex(@"^\s+- (S\d+\.\d+)(?:\s|$)")]
    private static partial Regex SubPoint();

    [GeneratedRegex(@"^S\d+(\.\d+)?$")]
    public static partial Regex IdShape();

    public static List<string> Parse(string specsMarkdown)
    {
        var ids = new List<string>();
        foreach (var line in specsMarkdown.Split('\n'))
        {
            if (line.Contains("(retired", StringComparison.Ordinal))
            {
                continue;
            }

            var match = TopLevel().Match(line);
            if (!match.Success)
            {
                match = SubPoint().Match(line);
            }

            if (match.Success)
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    private static (int, int) Key(string id)
    {
        var parts = id[1..].Split('.');
        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : -1);
    }
}

public static class PendingList
{
    public const string Path = "docs/spec-pending.txt";

    public static HashSet<string> Parse(string content, string source)
    {
        var ids = new HashSet<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!SpecIds.IdShape().IsMatch(line))
            {
                throw new SpecTraceException($"{source}: '{line}' is not an S-ID");
            }

            if (!ids.Add(line))
            {
                throw new SpecTraceException($"{source}: {line} is listed twice");
            }
        }

        return ids;
    }
}

/// <summary>specs.md and spec-pending.txt as they are on the base ref.</summary>
public sealed record GitBaseline(HashSet<string> Specs, HashSet<string>? Pending)
{
    public static GitBaseline Read(string root, string baseRef)
    {
        var (code, _) = Git(root, "rev-parse", "--verify", "--quiet", $"{baseRef}^{{commit}}");
        if (code != 0)
        {
            throw new SpecTraceException($"{baseRef} is not available; fetch it before running check");
        }

        var specs = Show(root, baseRef, SpecIds.SpecsPath);
        var pending = Show(root, baseRef, PendingList.Path);
        return new GitBaseline(
            specs is null ? [] : SpecIds.Parse(specs).ToHashSet(),
            pending is null ? null : PendingList.Parse(pending, $"{baseRef}:{PendingList.Path}"));
    }

    private static string? Show(string root, string baseRef, string path)
    {
        var (exists, _) = Git(root, "cat-file", "-e", $"{baseRef}:{path}");
        if (exists != 0)
        {
            return null;
        }

        var (code, output) = Git(root, "show", $"{baseRef}:{path}");
        return code == 0 ? output : throw new SpecTraceException($"git show {baseRef}:{path} failed");
    }

    private static (int Code, string Output) Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new SpecTraceException("could not start git");
        var stderr = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = stderr.Result;
        return (process.ExitCode, output);
    }
}

public sealed record TraitRefs(HashSet<string> Whole, HashSet<string> Partial)
{
    public IEnumerable<string> All => Whole.Union(Partial);
}

/// <summary>
/// Reads Spec/SpecPartial traits from the compiled Release assemblies of the tests/ projects in the solution.
/// </summary>
public static class TraitReader
{
    private const string TestsFolder = "tests/";

    public static TraitRefs Read(string root, bool noBuild, TextWriter log, SolutionBuilder build)
    {
        var solution = FindSolution(root);
        var projects = TestProjects(root, solution);

        var problems = AssemblyProblems(projects);
        if (problems.Count > 0)
        {
            if (noBuild)
            {
                throw new SpecTraceException($"test assemblies not usable and --no-build given: {string.Join("; ", problems)}");
            }

            log.WriteLine($"SpecTrace: building {Path.GetFileName(solution)} ({string.Join("; ", problems)})");
            var exit = build(root, Path.GetFileName(solution), log);
            if (exit != 0)
            {
                throw new SpecTraceException($"dotnet build exited {exit}");
            }

            problems = AssemblyProblems(projects);
            if (problems.Count > 0)
            {
                throw new SpecTraceException($"test assemblies still not usable after building: {string.Join("; ", problems)}");
            }
        }

        var refs = new TraitRefs([], []);
        foreach (var project in projects)
        {
            foreach (var assembly in project.Assemblies())
            {
                ReadAssembly(assembly, refs);
            }
        }

        return refs;
    }

    private static string FindSolution(string root)
    {
        var solutions = Directory.GetFiles(root, "*.slnx");
        return solutions.Length == 1
            ? solutions[0]
            : throw new SpecTraceException($"expected exactly one .slnx in {root}, found {solutions.Length}");
    }

    private static List<TestProject> TestProjects(string root, string solution)
    {
        var paths = XDocument.Load(solution)
            .Descendants("Project")
            .Select(p => ((string?)p.Attribute("Path"))?.Replace('\\', '/'))
            .OfType<string>()
            .Where(p => p.StartsWith(TestsFolder, StringComparison.Ordinal));
        return paths.Select(p => TestProject.Load(Path.Combine(root, p))).ToList();
    }

    private static List<string> AssemblyProblems(List<TestProject> projects)
    {
        var problems = new List<string>();
        foreach (var project in projects)
        {
            var assemblies = project.Assemblies();
            if (assemblies.Count == 0)
            {
                problems.Add($"{project.Name}: no Release assembly");
                continue;
            }

            var newestSource = project.NewestSourceWrite();
            if (assemblies.Any(a => File.GetLastWriteTimeUtc(a) < newestSource))
            {
                problems.Add($"{project.Name}: Release assembly older than its sources");
            }
        }

        return problems;
    }

    private static void ReadAssembly(string assemblyPath, TraitRefs refs)
    {
        using var context = new MetadataLoadContext(new PathAssemblyResolver(ResolverPaths(assemblyPath)));
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        foreach (var type in assembly.GetTypes())
        {
            var countingMethods = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(IsCountingTest)
                .ToList();
            if (countingMethods.Count == 0)
            {
                continue;
            }

            AddTraits(type.GetCustomAttributesData(), refs);
            foreach (var method in countingMethods)
            {
                AddTraits(method.GetCustomAttributesData(), refs);
            }
        }
    }

    private static bool IsCountingTest(MethodInfo method)
    {
        var testAttribute = method.GetCustomAttributesData().FirstOrDefault(a => IsTestAttribute(a.AttributeType));
        if (testAttribute is null)
        {
            return false;
        }

        foreach (var named in testAttribute.NamedArguments)
        {
            switch (named.MemberName)
            {
                case "Skip" or "SkipUnless" or "SkipWhen" when named.TypedValue.Value is not null:
                case "Explicit" when named.TypedValue.Value is true:
                    return false;
            }
        }

        return true;
    }

    private static bool IsTestAttribute(Type attributeType)
    {
        if (attributeType.Name == "PropertyAttribute")
        {
            return true;
        }

        for (Type? type = attributeType; type is not null; type = type.BaseType)
        {
            if (type.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTraits(IList<CustomAttributeData> attributes, TraitRefs refs)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != "Xunit.TraitAttribute" || attribute.ConstructorArguments.Count != 2)
            {
                continue;
            }

            var name = attribute.ConstructorArguments[0].Value as string;
            var value = attribute.ConstructorArguments[1].Value as string ?? "";
            var target = name switch
            {
                "Spec" => refs.Whole,
                "SpecPartial" => refs.Partial,
                _ => null,
            };
            if (target is null)
            {
                continue;
            }

            if (!SpecIds.IdShape().IsMatch(value))
            {
                throw new SpecTraceException($"[Trait(\"{name}\", \"{value}\")]: '{value}' is not an S-ID");
            }

            target.Add(value);
        }
    }

    private static IEnumerable<string> ResolverPaths(string assemblyPath)
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var frameworkDirs = new List<string> { runtimeDir };
        var sharedDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(runtimeDir)));
        if (sharedDir is not null && Directory.Exists(sharedDir))
        {
            foreach (var framework in Directory.GetDirectories(sharedDir))
            {
                var newest = Directory.GetDirectories(framework)
                    .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version())
                    .FirstOrDefault();
                if (newest is not null && !frameworkDirs.Contains(newest))
                {
                    frameworkDirs.Add(newest);
                }
            }
        }

        var localDir = Path.GetDirectoryName(assemblyPath)!;
        return frameworkDirs.Append(localDir).SelectMany(d => Directory.GetFiles(d, "*.dll"));
    }
}

public sealed record TestProject(string Directory, string Name, string AssemblyName)
{
    public static TestProject Load(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            throw new SpecTraceException($"solution lists {projectPath}, which does not exist");
        }

        var assemblyName = XDocument.Load(projectPath).Descendants("AssemblyName").Select(e => e.Value.Trim()).LastOrDefault();
        var name = Path.GetFileNameWithoutExtension(projectPath);
        return new TestProject(Path.GetDirectoryName(projectPath)!, name, string.IsNullOrEmpty(assemblyName) ? name : assemblyName);
    }

    public List<string> Assemblies()
    {
        var release = Path.Combine(Directory, "bin", "Release");
        if (!System.IO.Directory.Exists(release))
        {
            return [];
        }

        return System.IO.Directory.GetDirectories(release)
            .Select(tfm => Path.Combine(tfm, AssemblyName + ".dll"))
            .Where(File.Exists)
            .ToList();
    }

    public DateTime NewestSourceWrite()
    {
        var bin = Path.Combine(Directory, "bin") + Path.DirectorySeparatorChar;
        var obj = Path.Combine(Directory, "obj") + Path.DirectorySeparatorChar;
        return System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.StartsWith(bin, StringComparison.OrdinalIgnoreCase) && !f.StartsWith(obj, StringComparison.OrdinalIgnoreCase))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }
}

public static class DotnetBuild
{
    public static int Run(string root, string solutionFile, TextWriter log)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "build", solutionFile, "-c", "Release" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new SpecTraceException("could not start dotnet");
        process.OutputDataReceived += (_, e) => Forward(log, e.Data);
        process.ErrorDataReceived += (_, e) => Forward(log, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void Forward(TextWriter log, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (log)
        {
            log.WriteLine(line);
        }
    }
}

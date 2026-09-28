using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SpecTrace.Tests;

public sealed class SpecTraceTests : IDisposable
{
    private const string Specs = """
        # Specs

        - S1. First rule.
        - S2. Second rule:
          - S2.1 Sub-point one.
          - S2.2 Sub-point two.
        - S3. (retired, replaced by S4)
        - S4. A postcode that is retired (S102) is rejected.
        """;

    private static readonly string[] LiveIds = ["S1", "S2", "S2.1", "S2.2", "S4"];

    private readonly FixtureRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    // --- specs.md parsing ---

    [Fact]
    public void Parses_top_level_ids_and_sub_points_and_skips_retired_lines()
    {
        Assert.Equal(LiveIds, SpecIds.Parse(Specs));
    }

    [Fact]
    public void Parses_crlf_line_endings()
    {
        Assert.Equal(LiveIds, SpecIds.Parse(Specs.ReplaceLineEndings("\r\n")));
    }

    // --- check ---

    [Fact]
    public void Check_passes_when_every_live_id_is_tested_or_pending()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending("S2", "S2.1", "S2.2", "S4");
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Pass, result.Code);
        Assert.Contains("PASS", result.Out);
    }

    [Fact]
    public void Check_fails_for_an_id_neither_tested_nor_pending()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending("S2", "S2.1", "S2.2");
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("C17): S4", result.Out);
    }

    [Fact]
    public void Check_fails_for_a_sub_point_neither_tested_nor_pending()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending("S2", "S2.2", "S4");
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("C17): S2.1", result.Out);
    }

    [Fact]
    public void Check_fails_when_a_pending_id_has_a_spec_test()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending(LiveIds);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S2")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("have a Spec test", result.Out);
        Assert.Contains("(C68): S2", result.Out);
    }

    [Fact]
    public void Check_does_not_count_a_spec_partial_test_against_a_pending_id()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending(LiveIds);
        _repo.TestProject("A", TestClass("""[Fact, Trait("SpecPartial", "S2")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Pass, result.Code);
        Assert.Contains("IDs with only SpecPartial tests: S2", result.Out);
    }

    [Fact]
    public void Check_does_not_let_a_spec_partial_test_cover_an_id()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending("S2", "S2.1", "S2.2", "S4");
        _repo.TestProject("A", TestClass("""[Fact, Trait("SpecPartial", "S1")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("C17): S1", result.Out);
    }

    [Fact]
    public void Check_always_prints_ids_with_only_spec_partial_tests()
    {
        _repo.Base(Specs, pending: LiveIds);
        _repo.Pending("S2", "S2.1", "S2.2", "S4");
        _repo.TestProject("A", TestClass("""
            [Fact, Trait("Spec", "S1"), Trait("SpecPartial", "S1")] public void T() { }
            [Fact, Trait("SpecPartial", "S4"), Trait("SpecPartial", "S2.1")] public void U() { }
            """));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Pass, result.Code);
        Assert.Contains("IDs with only SpecPartial tests: S2.1, S4", result.Out);
    }

    [Fact]
    public void Check_fails_when_an_id_is_added_to_pending_that_is_not_new_in_specs()
    {
        _repo.Base(Specs, pending: ["S2", "S2.1", "S2.2", "S4"]);
        _repo.Pending(LiveIds);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("not new in docs/specs.md since origin/main (C68): S1", result.Out);
    }

    [Fact]
    public void Check_allows_adding_an_id_to_pending_that_is_new_in_specs()
    {
        _repo.Base(Specs.Replace("- S4. A postcode that is retired (S102) is rejected.", ""), pending: ["S1", "S2", "S2.1", "S2.2"]);
        _repo.Specs(Specs);
        _repo.Pending(LiveIds);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Pass, result.Code);
    }

    [Fact]
    public void Check_compares_additions_against_all_ids_when_main_has_no_pending_list()
    {
        _repo.Base(Specs, pending: null);
        _repo.Pending(LiveIds);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Pass, result.Code);
    }

    [Fact]
    public void Check_fails_for_a_pending_id_that_is_retired_or_not_in_specs()
    {
        _repo.Base(Specs, pending: null);
        _repo.Pending([.. LiveIds, "S3", "S9"]);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("not live in docs/specs.md: S3, S9", result.Out);
    }

    [Fact]
    public void Check_fails_for_a_test_referencing_an_id_not_in_specs()
    {
        _repo.Base(Specs, pending: null);
        _repo.Pending(LiveIds);
        _repo.TestProject("A", TestClass("""[Fact, Trait("SpecPartial", "S3")] public void T() { }"""));

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("tests reference IDs that are not live in docs/specs.md: S3", result.Out);
    }

    [Fact]
    public void Check_exits_2_when_origin_main_is_unavailable()
    {
        _repo.Specs(Specs);
        _repo.Pending(LiveIds);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Error, result.Code);
        Assert.Contains("origin/main is not available", result.Err);
    }

    [Fact]
    public void Check_exits_2_when_the_pending_list_is_missing()
    {
        _repo.Base(Specs, pending: null);

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Error, result.Code);
        Assert.Contains("docs/spec-pending.txt not found", result.Err);
    }

    [Fact]
    public void Check_exits_2_for_a_malformed_pending_list()
    {
        _repo.Base(Specs, pending: null);
        _repo.Pending("S1", "S2 # note");

        var result = _repo.Run("check");

        Assert.Equal(ExitCode.Error, result.Code);
    }

    // --- require ---

    [Theory]
    [InlineData("Spec")]
    [InlineData("SpecPartial")]
    public void Require_passes_for_an_id_with_a_spec_or_spec_partial_test(string trait)
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass($$"""[Fact, Trait("{{trait}}", "S2.1")] public void T() { }"""));

        var result = _repo.Run("require", "S2.1");

        Assert.Equal(ExitCode.Pass, result.Code);
    }

    [Fact]
    public void Require_fails_unless_every_id_has_a_test()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("require", "S1", "S2");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("FAIL S2", result.Out);
        Assert.DoesNotContain("FAIL S1", result.Out);
    }

    [Fact]
    public void Require_whole_passes_for_an_id_with_a_spec_test_that_is_not_pending()
    {
        _repo.Specs(Specs);
        _repo.Pending("S2");
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("require", "--whole", "S1");

        Assert.Equal(ExitCode.Pass, result.Code);
    }

    [Fact]
    public void Require_whole_fails_for_an_id_with_only_spec_partial_tests()
    {
        _repo.Specs(Specs);
        _repo.Pending("S2");
        _repo.TestProject("A", TestClass("""[Fact, Trait("SpecPartial", "S1")] public void T() { }"""));

        var result = _repo.Run("require", "--whole", "S1");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("FAIL S1: no Spec test", result.Out);
    }

    [Fact]
    public void Require_whole_fails_for_an_id_still_pending()
    {
        _repo.Specs(Specs);
        _repo.Pending("S1");
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var result = _repo.Run("require", "--whole", "S1");

        Assert.Equal(ExitCode.RuleFailure, result.Code);
        Assert.Contains("still listed", result.Out);
    }

    [Theory]
    [InlineData("S999")]
    [InlineData("S3")]
    [InlineData("S2.9")]
    [InlineData("s1")]
    public void Require_exits_2_for_an_id_that_is_not_live_in_specs(string id)
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        var plain = _repo.Run("require", "S1", id);
        var whole = _repo.Run("require", "--whole", id);

        Assert.Equal(ExitCode.Error, plain.Code);
        Assert.Equal(ExitCode.Error, whole.Code);
        Assert.Equal(0, _repo.Builds);
    }

    // --- exit-code contract ---

    [Theory]
    [InlineData]
    [InlineData("frobnicate")]
    [InlineData("require")]
    [InlineData("require", "--whole")]
    [InlineData("check", "S1")]
    [InlineData("check", "--whole")]
    [InlineData("require", "--bogus", "S1")]
    public void Usage_errors_exit_2(params string[] args)
    {
        _repo.Base(Specs, pending: null);
        _repo.Pending(LiveIds);

        var result = _repo.Run(args);

        Assert.Equal(ExitCode.Error, result.Code);
        Assert.Contains("usage:", result.Err);
    }

    [Fact]
    public void Missing_specs_exits_2()
    {
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        Assert.Equal(ExitCode.Error, _repo.Run("require", "S1").Code);
    }

    // --- which traits count ---

    [Theory]
    [InlineData("""[Fact(Skip = "later")]""")]
    [InlineData("""[Fact(SkipUnless = nameof(Flag))]""")]
    [InlineData("""[Fact(SkipWhen = nameof(Flag))]""")]
    [InlineData("""[Fact(Explicit = true)]""")]
    [InlineData("""[Theory(Skip = "later"), InlineData(1)]""")]
    [InlineData("""[FsCheck.Xunit.Property(Skip = "later")]""")]
    [InlineData("")]
    public void A_skipped_explicit_or_non_test_method_does_not_count(string testAttribute)
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass($$"""
            public static bool Flag => true;
            {{testAttribute}} [Trait("Spec", "S1")] public void T({{(testAttribute.Contains("InlineData") ? "int x" : "")}}) { }
            """));

        Assert.Equal(ExitCode.RuleFailure, _repo.Run("require", "S1").Code);
    }

    [Theory]
    [InlineData("""[Fact]""")]
    [InlineData("""[Fact(Explicit = false)]""")]
    [InlineData("""[Theory, InlineData(1)]""")]
    [InlineData("""[FsCheck.Xunit.Property]""")]
    public void A_runnable_test_method_counts(string testAttribute)
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass($$"""
            {{testAttribute}} [Trait("Spec", "S1")] public void T({{(testAttribute.Contains("InlineData") ? "int x" : "")}}) { }
            """));

        Assert.Equal(ExitCode.Pass, _repo.Run("require", "S1").Code);
    }

    [Fact]
    public void A_class_trait_counts_when_the_class_has_a_runnable_test()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", """
            using Xunit;
            namespace Fx;
            [Trait("Spec", "S1")]
            public class Tagged
            {
                [Fact(Skip = "later")] public void Skipped() { }
                [Fact] public void Runs() { }
            }
            """);

        Assert.Equal(ExitCode.Pass, _repo.Run("require", "S1").Code);
    }

    [Fact]
    public void A_class_trait_does_not_count_when_every_test_is_skipped()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", """
            using Xunit;
            namespace Fx;
            [Trait("Spec", "S1")]
            public class Tagged
            {
                [Fact(Skip = "later")] public void Skipped() { }
                public void Helper() { }
            }
            """);

        Assert.Equal(ExitCode.RuleFailure, _repo.Run("require", "S1").Code);
    }

    [Fact]
    public void A_tagged_method_in_a_file_no_project_compiles_does_not_count()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S2")] public void T() { }"""));
        _repo.File("tests/Stray.cs", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        Assert.Equal(ExitCode.RuleFailure, _repo.Run("require", "S1").Code);
        Assert.Equal(ExitCode.Pass, _repo.Run("require", "S2").Code);
    }

    [Fact]
    public void A_built_test_project_not_listed_in_the_solution_does_not_count()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S2")] public void T() { }"""));
        _repo.TestProject("Unlisted", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), listed: false);

        Assert.Equal(ExitCode.RuleFailure, _repo.Run("require", "S1").Code);
    }

    [Fact]
    public void A_listed_project_outside_tests_does_not_count()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S2")] public void T() { }"""));
        _repo.TestProject("Tool", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), folder: "tools");

        Assert.Equal(ExitCode.RuleFailure, _repo.Run("require", "S1").Code);
    }

    // --- missing or stale assemblies ---

    [Fact]
    public void A_missing_assembly_triggers_one_build()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), compile: false);

        var result = _repo.Run("require", "S1");

        Assert.Equal(ExitCode.Pass, result.Code);
        Assert.Equal(1, _repo.Builds);
    }

    [Fact]
    public void A_stale_assembly_triggers_one_build_and_the_new_traits_count()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S2")] public void T() { }"""));
        _repo.File("tests/A/Added.cs", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }""", "Added"));
        _repo.Touch("tests/A/Added.cs", DateTime.UtcNow.AddMinutes(5));

        var result = _repo.Run("require", "S1");

        Assert.Equal(ExitCode.Pass, result.Code);
        Assert.Equal(1, _repo.Builds);
    }

    [Fact]
    public void A_stale_project_file_triggers_a_build()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));
        _repo.Touch("tests/A/A.csproj", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(ExitCode.Pass, _repo.Run("require", "S1").Code);
        Assert.Equal(1, _repo.Builds);
    }

    [Fact]
    public void An_up_to_date_assembly_is_not_rebuilt()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""));

        Assert.Equal(ExitCode.Pass, _repo.Run("require", "S1").Code);
        Assert.Equal(0, _repo.Builds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_build_exits_2_for_a_missing_or_stale_assembly(bool stale)
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), compile: stale);
        if (stale)
        {
            _repo.Touch("tests/A/Test.cs", DateTime.UtcNow.AddMinutes(5));
        }

        var result = _repo.Run("require", "--no-build", "S1");

        Assert.Equal(ExitCode.Error, result.Code);
        Assert.Equal(0, _repo.Builds);
    }

    [Fact]
    public void An_assembly_still_missing_after_the_build_exits_2()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), compile: false);
        _repo.BuildCompiles = false;

        var result = _repo.Run("require", "S1");

        Assert.Equal(ExitCode.Error, result.Code);
        Assert.Equal(1, _repo.Builds);
    }

    [Fact]
    public void A_failed_build_exits_2()
    {
        _repo.Specs(Specs);
        _repo.TestProject("A", TestClass("""[Fact, Trait("Spec", "S1")] public void T() { }"""), compile: false);
        _repo.BuildExitCode = 1;

        Assert.Equal(ExitCode.Error, _repo.Run("check").Code);
    }

    private static string TestClass(string members, string name = "Tests") => $$"""
        using Xunit;
        namespace Fx;
        public class {{name}}
        {
            {{members}}
        }
        """;
}

internal sealed record RunResult(int Code, string Out, string Err);

/// <summary>A throwaway repository: specs, pending list, solution, test projects compiled with Roslyn, git base.</summary>
internal sealed class FixtureRepo : IDisposable
{
    private readonly List<(string Path, bool Listed)> _projects = [];

    public FixtureRepo() => File("Fixture.slnx", "<Solution>\n</Solution>\n");

    public string Root { get; } = Directory.CreateTempSubdirectory("spectrace-").FullName;

    public int Builds { get; private set; }

    public bool BuildCompiles { get; set; } = true;

    public int BuildExitCode { get; set; }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Specs(string content) => File("docs/specs.md", content);

    public void Pending(params string[] ids) => File("docs/spec-pending.txt", string.Join('\n', ids) + "\n");

    /// <summary>
    /// Commits specs (and a pending list, unless null) and points origin/main at that commit. The specs stay in the
    /// working tree; the pending list does not.
    /// </summary>
    public void Base(string specs, string[]? pending)
    {
        Git("init", "-q");
        Specs(specs);
        if (pending is not null)
        {
            Pending(pending);
        }

        Git("add", "-A");
        Git("-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid", "commit", "-q", "-m", "base");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        System.IO.File.Delete(Path.Combine(Root, "docs", "spec-pending.txt"));
    }

    public void TestProject(string name, string source, bool listed = true, string folder = "tests", bool compile = true)
    {
        var relative = $"{folder}/{name}/{name}.csproj";
        File(relative, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        File($"{folder}/{name}/Test.cs", source);
        _projects.Add((relative, listed));
        File("Fixture.slnx", "<Solution>\n" + string.Concat(
            _projects.Where(p => p.Listed).Select(p => $"  <Project Path=\"{p.Path}\" />\n")) + "</Solution>\n");
        if (compile)
        {
            Compile(relative);
        }
    }

    public void File(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
    }

    public void Touch(string relativePath, DateTime utc) =>
        System.IO.File.SetLastWriteTimeUtc(Path.Combine(Root, relativePath), utc);

    public RunResult Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = SpecTraceCli.Run(args, Root, stdout, stderr, FakeBuild);
        return new RunResult(code, stdout.ToString(), stderr.ToString());
    }

    private int FakeBuild(string root, string solutionFile, TextWriter log)
    {
        Assert.Equal(Root, root);
        Assert.Equal("Fixture.slnx", solutionFile);
        Builds++;
        if (BuildExitCode == 0 && BuildCompiles)
        {
            foreach (var (path, _) in _projects)
            {
                Compile(path);
            }
        }

        return BuildExitCode;
    }

    /// <summary>Compiles every .cs in the project's folder to bin/Release/net10.0, as dotnet build would.</summary>
    private void Compile(string projectRelativePath)
    {
        var projectDir = Path.GetDirectoryName(Path.Combine(Root, projectRelativePath))!;
        var name = Path.GetFileNameWithoutExtension(projectRelativePath);
        var sources = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(f), path: f))
            .Append(CSharpSyntaxTree.ParseText(PropertyAttributeSource));
        var compilation = CSharpCompilation.Create(
            name, sources, References.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var outDir = Path.Combine(projectDir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(outDir);
        var dll = Path.Combine(outDir, name + ".dll");
        var emit = compilation.Emit(dll);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var xunitDir = Path.GetDirectoryName(typeof(FactAttribute).Assembly.Location)!;
        foreach (var dependency in Directory.GetFiles(xunitDir, "xunit*.dll"))
        {
            System.IO.File.Copy(dependency, Path.Combine(outDir, Path.GetFileName(dependency)), overwrite: true);
        }

        var newestSource = Directory.GetFiles(projectDir, "*.cs*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(Path.Combine(projectDir, "bin"), StringComparison.OrdinalIgnoreCase))
            .Max(System.IO.File.GetLastWriteTimeUtc);
        System.IO.File.SetLastWriteTimeUtc(dll, newestSource.AddSeconds(1));
    }

    /// <summary>Stands in for FsCheck.Xunit's attribute, which is recognised by name.</summary>
    private const string PropertyAttributeSource = """
        namespace FsCheck.Xunit
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class PropertyAttribute : System.Attribute
            {
                public string Skip { get; set; }
            }
        }
        """;

    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(FactAttribute).Assembly.Location)
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    private void Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, ".no-global-gitconfig");
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)}: {stderr.Result}");
    }
}

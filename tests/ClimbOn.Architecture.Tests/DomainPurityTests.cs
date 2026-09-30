using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Conditions;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace ClimbOn.Architecture.Tests;

// C19: the rules are built from a type selection so the same rule runs against the real
// layers and against the Violations assembly, which proves each rule can fail.
public sealed class DomainPurityTests
{
    private const string ViolationsNamespace = "ClimbOn.Architecture.Violations";

    private static readonly Assembly DomainAssembly = Assembly.Load("ClimbOn.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("ClimbOn.Application");
    private static readonly Assembly ViolationsAssembly = Assembly.Load("ClimbOn.Architecture.Violations");

    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, ViolationsAssembly)
        .Build();

    private static readonly CompilerGeneratedStructs Structs =
        new(DomainAssembly, ApplicationAssembly, ViolationsAssembly);

    // Types the compiler emits into every assembly (nullable attributes and their marker).
    private const string CompilerGenerated = @"^Microsoft\.CodeAnalysis\.EmbeddedAttribute$|^<PrivateImplementationDetails>";

    private const string Bcl =
        @"^System\.(?!Net\.|Data\.|IO\.(File|Directory|Drive|Path$|Enumeration\.|MemoryMappedFiles\.|Pipes\.|IsolatedStorage\.))";

    private static readonly Regex ClockMethods = new(
        @" System\.DateTime::get_(Now|UtcNow|Today)\(\)$"
        + @"| System\.DateTimeOffset::get_(Now|UtcNow)\(\)$"
        + @"| System\.TimeProvider::get_System\(\)$"
        + @"| System\.Environment::get_TickCount(64)?\(\)$"
        + @"| System\.Threading\.Tasks\.Task::Delay\("
        + @"| System\.Threading\.Thread::Sleep\(");

    private static readonly Regex ClockTypes =
        new(@"^System\.Diagnostics\.Stopwatch$|^System\.Threading\.(Periodic)?Timer$|^System\.Timers\.");

    private static readonly Regex OuterLayers = new(@"^ClimbOn\.(Infrastructure|Api)\.");

    // ArchUnitNET's type-provider conditions only see types inside the loaded architecture,
    // so the rules inspect each dependency's target directly to cover referenced BCL types.
    private static IArchRule DependsOnlyOnBcl(GivenTypesConjunction types, string ownNamespace)
    {
        var allowed = new Regex($@"{Bcl}|{CompilerGenerated}|^{Regex.Escape(ownNamespace)}\.");
        return Forbid(types, target => !allowed.IsMatch(target.Type),
            "depend only on the BCL outside System.Net, System.Data and file-system System.IO");
    }

    private static IArchRule NeverReadsSystemClock(GivenTypesConjunction types) =>
        Forbid(types,
            target => ClockTypes.IsMatch(target.Type)
                || (target.Member is { } member && ClockMethods.IsMatch(member)),
            "never read the system clock or wait on real time");

    private static IArchRule DoesNotReferenceOuterLayers(GivenTypesConjunction types) =>
        Forbid(types, target => OuterLayers.IsMatch(target.Type),
            "not depend on Infrastructure or Api");

    private static IEnumerable<Target> Targets(IType type) =>
        type.Dependencies
            .Select(dependency => new Target(
                dependency.Target.FullName,
                dependency is MethodCallDependency call ? call.TargetMember.FullName : null))
            .Concat(Structs.Of(type));

    private static IArchRule Forbid(GivenTypesConjunction types, Func<Target, bool> isForbidden, string rule) =>
        types.Should()
            .FollowCustomCondition(
                type =>
                {
                    var offending = Targets(type).Where(isForbidden)
                        .Select(target => target.Member ?? target.Type)
                        .Distinct()
                        .ToList();
                    return new ConditionResult(type, offending.Count == 0, "uses " + string.Join(", ", offending));
                },
                rule)
            .WithoutRequiringPositiveResults();

    [Fact]
    public void Domain_depends_only_on_BCL() =>
        DependsOnlyOnBcl(Types().That().ResideInAssembly(DomainAssembly), "ClimbOn.Domain").Check(Architecture);

    [Fact]
    public void Domain_never_reads_system_clock() =>
        NeverReadsSystemClock(Types().That().ResideInAssembly(DomainAssembly)).Check(Architecture);

    [Fact]
    public void Application_does_not_reference_outer_layers() =>
        DoesNotReferenceOuterLayers(Types().That().ResideInAssembly(ApplicationAssembly)).Check(Architecture);

    public static TheoryData<string, string[]> Violations => new()
    {
        {
            "Dependencies",
            [
                "UsesDatabase", "UsesFileSystem", "UsesHttp", "UsesHttpAsTypeArgument", "UsesHttpInAsyncMethod",
                "UsesNonBclType",
            ]
        },
        {
            "Clock",
            [
                "CallsTaskDelay", "CallsThreadSleep", "ReadsDateTimeNow", "ReadsDateTimeOffsetNow",
                "ReadsDateTimeOffsetUtcNow", "ReadsDateTimeToday", "ReadsDateTimeUtcNow", "ReadsDateTimeUtcNowInAsyncMethod", "ReadsTickCount",
                "ReadsTickCount64", "ReadsTimeProviderSystem", "UsesPeriodicTimer", "UsesStopwatch",
                "UsesThreadingTimer", "UsesTimersTimer",
            ]
        },
        {
            "Layers",
            ["UsesApi", "UsesApiInAsyncMethod", "UsesInfrastructure"]
        },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void Rules_detect_violations(string group, string[] expected)
    {
        var types = Types().That().ResideInNamespace($"{ViolationsNamespace}.{group}");
        var rule = group switch
        {
            "Dependencies" => DependsOnlyOnBcl(types, ViolationsNamespace),
            "Clock" => NeverReadsSystemClock(types),
            _ => DoesNotReferenceOuterLayers(types),
        };

        var offenders = rule.Evaluate(Architecture)
            .Where(result => !result.Passed)
            .Select(result => TopLevelName((IType)result.EvaluatedObject))
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, offenders);
    }

    // A call inside a lambda or a Debug-built async method belongs to a nested compiler-generated type.
    private static string TopLevelName(IType type) => type.Name.Split('+')[0];
}

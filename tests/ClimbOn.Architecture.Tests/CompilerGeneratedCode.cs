using Mono.Cecil;
using Assembly = System.Reflection.Assembly;
using IType = ArchUnitNET.Domain.IType;

namespace ClimbOn.Architecture.Tests;

// A dependency the rules judge: the target type, the assembly it comes from and, for a call,
// the called member.
internal sealed record Target(string Type, string Assembly, string? Member);

// The compiler moves method bodies into generated types (async and iterator state machines,
// closures) and generated methods (lambdas, local functions), and ArchUnitNET misses several
// of those shapes. Rather than chase each one, this reads all generated code with Cecil and
// attributes its dependencies to the source type that declares it.
internal sealed class CompilerGeneratedCode
{
    private readonly Dictionary<string, List<Target>> _targetsByType = [];

    public CompilerGeneratedCode(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.Types.SelectMany(Nested).Where(type => !IsCompilerGenerated(type)))
            {
                var generated = type.NestedTypes.Where(IsCompilerGenerated).SelectMany(Nested).ToList();
                var targets = TargetsOf(
                        generated.SelectMany(nested => nested.Fields),
                        generated.SelectMany(nested => nested.Methods)
                            .Concat(type.Methods.Where(method => method.Name.StartsWith('<'))))
                    .ToList();
                if (targets.Count > 0)
                {
                    _targetsByType[Name(type)] = targets;
                }
            }
        }
    }

    public IEnumerable<Target> Of(IType type) =>
        _targetsByType.TryGetValue(type.FullName, out var targets) ? targets : [];

    // C# cannot name a type or method starting with '<'; only the compiler does.
    private static bool IsCompilerGenerated(TypeDefinition type) => type.Name.StartsWith('<');

    private static IEnumerable<TypeDefinition> Nested(TypeDefinition type) =>
        type.NestedTypes.SelectMany(Nested).Prepend(type);

    private static IEnumerable<Target> TargetsOf(IEnumerable<FieldDefinition> fields, IEnumerable<MethodDefinition> methods)
    {
        var bodies = methods.Where(method => method.HasBody).Select(method => method.Body).ToList();
        var types = fields.Select(field => field.FieldType)
            .Concat(bodies.SelectMany(body => body.Variables.Select(variable => variable.VariableType)));
        var targets = types.SelectMany(Expand).Select(type => Of(type, null)).ToList();

        foreach (var instruction in bodies.SelectMany(body => body.Instructions))
        {
            switch (instruction.Operand)
            {
                case MethodReference method:
                    targets.AddRange(Expand(method.DeclaringType).Select(type => Of(type, method.FullName)));
                    targets.AddRange(Signature(method).SelectMany(Expand).Select(type => Of(type, null)));
                    break;
                case FieldReference field:
                    targets.AddRange(Expand(field.DeclaringType).Concat(Expand(field.FieldType))
                        .Select(type => Of(type, null)));
                    break;
                case TypeReference reference:
                    targets.AddRange(Expand(reference).Select(type => Of(type, null)));
                    break;
            }
        }

        return targets;
    }

    private static IEnumerable<TypeReference> Signature(MethodReference method) =>
        method.Parameters.Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .Concat(method is GenericInstanceMethod generic ? generic.GenericArguments : []);

    // Every named type a reference mentions: the type itself and its generic arguments.
    private static IEnumerable<TypeReference> Expand(TypeReference type) => type switch
    {
        GenericParameter => [],
        GenericInstanceType generic => generic.GenericArguments.SelectMany(Expand).Prepend(generic.ElementType),
        TypeSpecification specification => Expand(specification.ElementType),
        _ => [type],
    };

    private static Target Of(TypeReference type, string? member) =>
        new(Name(type), type.Scope is ModuleDefinition module ? module.Assembly.Name.Name : type.Scope.Name, member);

    // ArchUnitNET separates nested types with '+', Cecil with '/'.
    private static string Name(TypeReference type) => type.FullName.Replace('/', '+');
}

using Mono.Cecil;
using Assembly = System.Reflection.Assembly;
using IType = ArchUnitNET.Domain.IType;

namespace ClimbOn.Architecture.Tests;

// A dependency the rules judge: the target type and, for a call, the called member.
internal sealed record Target(string Type, string? Member);

// ArchUnitNET does not load compiler-generated structs. In Release an async method's body
// lives in one (its state machine), so the rules would never see it. This reads those
// structs with Cecil and attributes their dependencies to the type that declares them.
internal sealed class CompilerGeneratedStructs
{
    private readonly Dictionary<string, List<Target>> _targetsByType = [];

    public CompilerGeneratedStructs(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.Types.SelectMany(Nested))
            {
                var targets = type.NestedTypes.Where(IsCompilerGeneratedStruct)
                    .SelectMany(Nested)
                    .SelectMany(TargetsOf)
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

    private static bool IsCompilerGeneratedStruct(TypeDefinition type) =>
        type.IsValueType && type.Name.StartsWith('<');

    private static IEnumerable<TypeDefinition> Nested(TypeDefinition type) =>
        type.NestedTypes.SelectMany(Nested).Prepend(type);

    private static IEnumerable<Target> TargetsOf(TypeDefinition type)
    {
        var types = type.Fields.Select(field => field.FieldType)
            .Concat(type.Methods.Where(method => method.HasBody)
                .SelectMany(method => method.Body.Variables.Select(variable => variable.VariableType)));
        var targets = types.SelectMany(Expand).Select(name => new Target(name, null)).ToList();

        foreach (var instruction in type.Methods.Where(method => method.HasBody)
                     .SelectMany(method => method.Body.Instructions))
        {
            switch (instruction.Operand)
            {
                case MethodReference method:
                    targets.AddRange(Expand(method.DeclaringType)
                        .Select(name => new Target(name, method.FullName)));
                    targets.AddRange(Signature(method).SelectMany(Expand).Select(name => new Target(name, null)));
                    break;
                case FieldReference field:
                    targets.AddRange(Expand(field.DeclaringType).Concat(Expand(field.FieldType))
                        .Select(name => new Target(name, null)));
                    break;
                case TypeReference reference:
                    targets.AddRange(Expand(reference).Select(name => new Target(name, null)));
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
    private static IEnumerable<string> Expand(TypeReference type) => type switch
    {
        GenericParameter => [],
        GenericInstanceType generic => generic.GenericArguments.SelectMany(Expand)
            .Prepend(Name(generic.ElementType)),
        TypeSpecification specification => Expand(specification.ElementType),
        _ => [Name(type)],
    };

    // ArchUnitNET separates nested types with '+', Cecil with '/'.
    private static string Name(TypeReference type) => type.FullName.Replace('/', '+');
}

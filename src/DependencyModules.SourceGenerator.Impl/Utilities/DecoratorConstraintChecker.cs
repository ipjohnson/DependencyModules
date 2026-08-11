using CSharpAuthor;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Whether a generic decorator can legally be closed over a registration's type arguments.
/// </summary>
/// <remarks>
/// <para>
/// A decorator may constrain its type parameters more tightly than the service does —
/// <c>Logging&lt;T&gt; : IHandler&lt;T&gt; where T : class</c> is ordinary, and so is a registration
/// of <c>IHandler&lt;int&gt;</c>. Both declarations are legal; closing one over the other is not,
/// and emitting it anyway produces <b>CS0452 in generated code</b>, which is the failure this
/// generator is built never to produce.
/// </para>
/// <para>
/// Checked against symbols rather than against the rendered type names, because the question —
/// is this a reference type, does it implement that interface — is a semantic one. A closing that
/// cannot be resolved is allowed rather than dropped: the compiler will say so at the call site,
/// which is better than a decoration going missing for a reason nothing reports.
/// </para>
/// </remarks>
public static class DecoratorConstraintChecker {

    public static bool CanClose(
        Compilation compilation, ITypeDefinition decoratorType, GenericTypeDefinition closedService) {

        var decorator = Resolve(compilation, decoratorType);

        if (decorator == null || decorator.TypeParameters.Length != closedService.TypeArguments.Count) {
            return true;
        }

        for (var i = 0; i < decorator.TypeParameters.Length; i++) {
            var argument = Resolve(compilation, closedService.TypeArguments[i]);

            if (argument != null && !Satisfies(decorator.TypeParameters[i], argument)) {
                return false;
            }
        }

        return true;
    }

    private static bool Satisfies(ITypeParameterSymbol parameter, INamedTypeSymbol argument) {
        if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType) {
            return false;
        }

        if (parameter.HasValueTypeConstraint && !argument.IsValueType) {
            return false;
        }

        if (parameter.HasConstructorConstraint &&
            !argument.InstanceConstructors.Any(
                constructor => constructor.Parameters.Length == 0 &&
                               constructor.DeclaredAccessibility == Accessibility.Public)) {
            return false;
        }

        foreach (var constraint in parameter.ConstraintTypes) {
            if (!Implements(argument, constraint)) {
                return false;
            }
        }

        return true;
    }

    private static bool Implements(INamedTypeSymbol argument, ITypeSymbol constraint) {
        // A constraint naming another type parameter cannot be checked without the whole
        // substitution, and the service's own constraints already cover the usual case.
        if (constraint is ITypeParameterSymbol) {
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(argument, constraint)) {
            return true;
        }

        foreach (var implemented in argument.AllInterfaces) {
            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, constraint.OriginalDefinition)) {
                return true;
            }
        }

        for (var baseType = argument.BaseType; baseType != null; baseType = baseType.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, constraint.OriginalDefinition)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The C# keyword spellings, which is how a primitive type argument reaches here.
    /// </summary>
    /// <remarks>
    /// <c>IHandler&lt;int&gt;</c> renders its argument as <c>int</c> with no namespace, and
    /// <c>GetTypeByMetadataName("int")</c> finds nothing — so a value type would look unresolvable
    /// and be allowed through, which is exactly the case this class exists to catch.
    /// </remarks>
    private static readonly Dictionary<string, string> Aliases = new() {
        ["bool"] = "System.Boolean", ["byte"] = "System.Byte", ["sbyte"] = "System.SByte",
        ["char"] = "System.Char", ["decimal"] = "System.Decimal", ["double"] = "System.Double",
        ["float"] = "System.Single", ["int"] = "System.Int32", ["uint"] = "System.UInt32",
        ["long"] = "System.Int64", ["ulong"] = "System.UInt64", ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16", ["nint"] = "System.IntPtr", ["nuint"] = "System.UIntPtr",
        ["object"] = "System.Object", ["string"] = "System.String",
    };

    private static INamedTypeSymbol? Resolve(Compilation compilation, ITypeDefinition type) {
        var name = string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;

        if (Aliases.TryGetValue(name, out var metadataName)) {
            name = metadataName;
        }

        if (type is GenericTypeDefinition { TypeArguments.Count: > 0 } generic) {
            name += "`" + generic.TypeArguments.Count;
        }

        return compilation.GetTypeByMetadataName(name);
    }
}

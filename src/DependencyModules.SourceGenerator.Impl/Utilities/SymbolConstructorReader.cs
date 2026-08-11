using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads the constructor the container would pick, from a symbol rather than from syntax.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServiceModelUtility.GetConstructorInfo"/> reads a declaration, which only works for a
/// type declared in the compilation being built. Three things need a constructor for a type named
/// rather than declared: <c>[Decorate(typeof(IFoo), typeof(FooDecorator))]</c> on a module, which
/// names its decorator by <c>typeof</c>; convention scanning of a referenced assembly; and reading a
/// package's <c>[Decorator]</c> across an assembly boundary.
/// </para>
/// <para>
/// It exists because generated code constructs the decorator with a literal <c>new</c>. The
/// alternative is <c>ActivatorUtilities</c> over a <see cref="Type"/> at run time, which reflects on
/// every resolution and is the shape a published Native AOT build has no code for.
/// </para>
/// </remarks>
public static class SymbolConstructorReader {

    private const string ActivatorUtilitiesConstructor = "ActivatorUtilitiesConstructorAttribute";

    private const string FromKeyedServices = "FromKeyedServicesAttribute";

    /// <summary>
    /// The constructor to emit a <c>new</c> for, or null when the type has no public one.
    /// </summary>
    /// <remarks>
    /// Null is the answer to "this cannot be constructed by generated code", and the caller reports
    /// it. Falling back to something reflective would trade a build error for a failure at resolve
    /// time in a published application.
    /// </remarks>
    public static ConstructorInfoModel? Read(INamedTypeSymbol type) {
        var chosen = Choose(type);

        return chosen == null ? null : new ConstructorInfoModel(Parameters(chosen));
    }

    /// <summary>
    /// <c>[ActivatorUtilitiesConstructor]</c> if one is marked, otherwise the greediest public one.
    /// </summary>
    /// <remarks>
    /// Same precedence the syntax path applies, and the same the container would apply. A type that
    /// opted into a specific constructor and silently got a different one is the kind of difference
    /// nobody looks for.
    /// </remarks>
    private static IMethodSymbol? Choose(INamedTypeSymbol type) {
        IMethodSymbol? greediest = null;

        foreach (var constructor in type.InstanceConstructors) {
            if (constructor.DeclaredAccessibility != Accessibility.Public || constructor.IsStatic) {
                continue;
            }

            foreach (var attribute in constructor.GetAttributes()) {
                if (attribute.AttributeClass?.Name == ActivatorUtilitiesConstructor) {
                    return constructor;
                }
            }

            if (greediest == null || constructor.Parameters.Length > greediest.Parameters.Length) {
                greediest = constructor;
            }
        }

        return greediest;
    }

    private static IReadOnlyList<ParameterInfoModel> Parameters(IMethodSymbol constructor) {
        var parameters = new List<ParameterInfoModel>(constructor.Parameters.Length);

        foreach (var parameter in constructor.Parameters) {
            parameters.Add(new ParameterInfoModel(
                parameter.Name,
                TypeOf(parameter),
                parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null,
                Attributes(parameter)));
        }

        return parameters;
    }

    /// <summary>
    /// The parameter's type, carrying its nullability.
    /// </summary>
    /// <remarks>
    /// Nullability is not decoration here: it decides whether the emitted call resolves with
    /// <c>GetService</c> or <c>GetRequiredService</c>, so an optional dependency that lost its
    /// annotation would start throwing when the container simply does not have one.
    /// </remarks>
    private static ITypeDefinition TypeOf(IParameterSymbol parameter) {
        var definition = parameter.Type.GetTypeDefinition();

        return parameter.NullableAnnotation == NullableAnnotation.Annotated
            ? definition.MakeNullable()
            : definition;
    }

    /// <summary>
    /// The parameter attributes the emitted call has to honour.
    /// </summary>
    /// <remarks>
    /// Only <c>[FromKeyedServices]</c>, and only its key. Everything else on a parameter is the
    /// declaring assembly's business; this one changes which registration the generated code
    /// resolves, and getting it wrong returns the right type and the wrong instance with nothing
    /// reported.
    /// </remarks>
    private static IReadOnlyList<AttributeModel> Attributes(IParameterSymbol parameter) {
        List<AttributeModel>? attributes = null;

        foreach (var attribute in parameter.GetAttributes()) {
            if (attribute.AttributeClass?.Name != FromKeyedServices ||
                attribute.ConstructorArguments.Length == 0) {
                continue;
            }

            attributes ??= new List<AttributeModel>(1);

            attributes.Add(new AttributeModel(
                KnownTypes.Microsoft.DependencyInjection.FromKeyedServicesAttribute,
                new[] { new AttributeArgumentValue("key", attribute.ConstructorArguments[0].Value) },
                Array.Empty<AttributeArgumentValue>(),
                Array.Empty<ITypeDefinition>()));
        }

        return (IReadOnlyList<AttributeModel>?)attributes ?? Array.Empty<AttributeModel>();
    }
}

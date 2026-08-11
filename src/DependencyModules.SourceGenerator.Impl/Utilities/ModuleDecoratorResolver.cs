using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Fills in what <c>[Decorate(typeof(IFoo), typeof(FooDecorator))]</c> cannot say.
/// </summary>
/// <remarks>
/// <para>
/// The attribute names both types and nothing else. <c>[Decorator]</c> on a class is read from the
/// declaration, so its constructor comes for free; this form names a type that may be declared
/// anywhere, including in a referenced assembly — which is the reason the module-level form exists.
/// </para>
/// <para>
/// So the constructor is looked up from the compilation. That is the only way to emit a literal
/// <c>new</c> for it, and emitting one is what makes the decoration survive publishing.
/// </para>
/// </remarks>
public static class ModuleDecoratorResolver {

    /// <summary>
    /// A resolved decorator, or the reason it could not be.
    /// </summary>
    /// <param name="Reason">
    /// Null when <paramref name="Model"/> can be emitted as a closed call. Otherwise why not,
    /// for the log — the decoration is simply not emitted.
    /// </param>
    public record Resolution(DecoratorModel Model, string? Reason);

    /// <summary>
    /// Resolves every <c>[Decorate]</c> a module declares.
    /// </summary>
    public static IReadOnlyList<Resolution> Resolve(
        ModuleEntryPointModel entryPointModel,
        Compilation compilation,
        CancellationToken cancellationToken) {

        var resolutions = new List<Resolution>();

        foreach (var decorator in DecoratorModelUtility.GetModuleDeclaredDecorators(entryPointModel)) {
            cancellationToken.ThrowIfCancellationRequested();

            resolutions.Add(Resolve(decorator, compilation));
        }

        return resolutions;
    }

    private static Resolution Resolve(DecoratorModel decorator, Compilation compilation) {
        var symbol = Find(compilation, decorator.DecoratorType);

        if (symbol == null) {
            return new Resolution(
                decorator, "its type could not be resolved from this compilation or its references");
        }

        var constructor = SymbolConstructorReader.Read(symbol);

        if (constructor == null) {
            return new Resolution(decorator, "it has no public constructor");
        }

        var innerIndex = IndexOfInner(constructor, decorator.ServiceType);

        if (innerIndex < 0) {
            return new Resolution(
                decorator,
                $"no constructor parameter takes '{decorator.ServiceType.Name}', so there is nowhere " +
                "to pass the instance being wrapped");
        }

        return new Resolution(
            decorator with { Constructor = constructor, InnerParameterIndex = innerIndex },
            null);
    }

    /// <summary>
    /// Which constructor parameter takes the service being wrapped.
    /// </summary>
    /// <remarks>
    /// A generic decorator declares the parameter closed over its own type parameters —
    /// <c>IHandler&lt;T&gt;</c> — while the attribute named the unbound <c>IHandler&lt;&gt;</c>.
    /// Those never compare equal, so the comparison is made on the unbound form of both. The stored
    /// parameter type keeps its names, because closing the decorator over a registration reads the
    /// type parameter order back off it.
    /// </remarks>
    private static int IndexOfInner(ConstructorInfoModel constructor, ITypeDefinition serviceType) {
        var wanted = serviceType.ToUnboundGeneric();

        for (var i = 0; i < constructor.Parameters.Count; i++) {
            var parameterType = constructor.Parameters[i].ParameterType.MakeNullable(false);

            if (parameterType.Equals(serviceType) || parameterType.ToUnboundGeneric().Equals(wanted)) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The symbol for a type the attribute named.
    /// </summary>
    /// <remarks>
    /// A generic decorator arrives with its arguments blanked, because an unbound generic is what a
    /// <c>typeof</c> can carry — so the metadata name needs the arity back on it.
    /// </remarks>
    private static INamedTypeSymbol? Find(Compilation compilation, ITypeDefinition type) {
        var name = string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;

        if (type is GenericTypeDefinition { TypeArguments.Count: > 0 } generic) {
            name += "`" + generic.TypeArguments.Count;
        }

        return compilation.GetTypeByMetadataName(name);
    }
}

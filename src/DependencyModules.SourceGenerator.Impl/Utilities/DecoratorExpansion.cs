using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Turns each generic decorator into one decoration per closed registration it applies to.
/// </summary>
/// <remarks>
/// <para>
/// A non-generic decorator passes through unchanged: it already names one service type. A generic one
/// names an open generic, and there is no closed call to emit for that — so it is expanded against
/// the registrations that close it.
/// </para>
/// <para>
/// Shared by both generators, because both hold registrations the other cannot see: the attribute
/// path has what <c>[SingletonService]</c> and friends registered, the convention path has what
/// <c>RegisterAll</c> matched. Each expands the same declaration against its own set, and
/// <c>DecoratorHelper</c> refuses to apply one decorator to a descriptor twice where the two sets
/// name the same closed service.
/// </para>
/// </remarks>
public static class DecoratorExpansion {

    public static IReadOnlyList<DecoratorModel> Expand(
        IReadOnlyList<DecoratorModel> decorators,
        IReadOnlyList<ITypeDefinition> registeredServiceTypes,
        bool includeNonGeneric = true,
        Func<ITypeDefinition, GenericTypeDefinition, bool>? canClose = null) {

        var expanded = new List<DecoratorModel>(decorators.Count);

        foreach (var decorator in decorators) {
            if (decorator.IsIgnored) {
                continue;
            }

            if (!decorator.IsOpenGeneric) {
                // A non-generic decorator names one service type and needs no expansion, so only
                // the pass that owns the declaration emits it. Anything that cannot be constructed
                // by generated code is dropped: generated code builds the decorator with a literal
                // new, and the reflective overload that used to stand in for this is gone because
                // it never worked in a published application.
                if (includeNonGeneric && decorator.CanMonomorphise) {
                    expanded.Add(decorator);
                }

                continue;
            }

            foreach (var serviceType in registeredServiceTypes) {
                if (!ClosesTheSameGeneric(serviceType, decorator.ServiceType)) {
                    continue;
                }

                var closedService = (GenericTypeDefinition)serviceType;

                // A decorator may constrain its type parameters more tightly than the service does.
                // Closing it over an argument that violates one emits code that does not compile.
                if (canClose != null && !canClose(decorator.DecoratorType, closedService)) {
                    continue;
                }

                var closed = DecoratorTypeUtility.Close(decorator, closedService);

                if (closed == null) {
                    continue;
                }

                expanded.Add(closed);
            }
        }

        return expanded;
    }

    /// <summary>
    /// Whether a registered service type is a closed construction of the decorated open generic.
    /// </summary>
    private static bool ClosesTheSameGeneric(ITypeDefinition registered, ITypeDefinition decorated) =>
        registered is GenericTypeDefinition closed &&
        decorated is GenericTypeDefinition open &&
        closed.TypeArguments.Count == open.TypeArguments.Count &&
        closed.Name == open.Name &&
        closed.Namespace == open.Namespace &&
        // The decorated form has its arguments blanked; a registration that also has them blanked is
        // an open generic registration, which cannot be decorated at all.
        closed.TypeArguments.Any(argument => !string.IsNullOrEmpty(argument.Name));
}

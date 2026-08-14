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

    /// <param name="refusedForOpenGenericRegistration">
    /// Decorators that name a service the compilation registers as an open generic. Nothing can be
    /// emitted for those — see <see cref="NamesAnOpenGenericRegistration"/> — and the caller reports
    /// DM0013 rather than letting them disappear.
    /// </param>
    public static IReadOnlyList<DecoratorModel> Expand(
        IReadOnlyList<DecoratorModel> decorators,
        IReadOnlyList<ITypeDefinition> registeredServiceTypes,
        out IReadOnlyList<DecoratorModel> refusedForOpenGenericRegistration,
        bool includeNonGeneric = true,
        Func<ITypeDefinition, GenericTypeDefinition, bool>? canClose = null) {

        var expanded = new List<DecoratorModel>(decorators.Count);
        List<DecoratorModel>? refused = null;

        foreach (var decorator in decorators) {
            if (decorator.IsIgnored) {
                continue;
            }

            if (!decorator.IsOpenGeneric) {
                // An unbound service type has no legal emission at all: Decorate<IHolder<>> is
                // CS7003. A generic decorator reaches this state only when nothing closed it, and is
                // handled below; a non-generic one never had an expansion step to catch it, so this
                // is where it stops. Refused whatever is registered, because the emission is invalid
                // on its own terms.
                if (decorator.HasUnboundServiceType) {
                    (refused ??= new List<DecoratorModel>()).Add(decorator);

                    continue;
                }

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

            var closedCount = 0;

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
                closedCount++;
            }

            // Nothing closed it. Distinguishing the two reasons is the whole point: a compilation
            // that registers the service as an open generic can never be decorated and is worth
            // reporting, while a compilation that registers nothing at all is the ordinary
            // cross-assembly case — [Decorate] exists to name a service someone else registers, so
            // reporting that would fire on the feature's primary use.
            if (closedCount == 0 && NamesAnOpenGenericRegistration(decorator, registeredServiceTypes)) {
                (refused ??= new List<DecoratorModel>()).Add(decorator);
            }
        }

        refusedForOpenGenericRegistration = (IReadOnlyList<DecoratorModel>?)refused ?? Array.Empty<DecoratorModel>();

        return expanded;
    }

    /// <summary>
    /// Whether this compilation registers the decorated service as an open generic.
    /// </summary>
    /// <remarks>
    /// Matched on name, namespace and arity, with every type argument blank — the form
    /// <c>services.AddSingleton(typeof(IStore&lt;&gt;), typeof(Store&lt;&gt;))</c> produces.
    /// </remarks>
    private static bool NamesAnOpenGenericRegistration(
        DecoratorModel decorator, IReadOnlyList<ITypeDefinition> registeredServiceTypes) {

        if (decorator.ServiceType is not GenericTypeDefinition decorated) {
            return false;
        }

        foreach (var registered in registeredServiceTypes) {
            if (registered is GenericTypeDefinition open &&
                open.TypeArguments.Count == decorated.TypeArguments.Count &&
                open.Name == decorated.Name &&
                open.Namespace == decorated.Namespace &&
                open.TypeArguments.All(argument => string.IsNullOrEmpty(argument.Name))) {

                return true;
            }
        }

        return false;
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

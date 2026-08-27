using System.Linq;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// A decorator to apply to a service, from either <c>[Decorator]</c> on the decorator class or
/// <c>[Decorate]</c> on a module.
/// </summary>
/// <param name="ServiceType">The decorated service. May be an open generic.</param>
/// <param name="DecoratorType">The decorator wrapping it.</param>
/// <param name="Order">
/// Lower values are applied first and sit closer to the implementation. Compared across every
/// module, not only within the declaring one.
/// </param>
/// <param name="Realm">Restricts the decorator to one module, matching the service Realm property.</param>
/// <param name="Conditions">
/// Environment conditions read from the decorator class, combining with <b>and</b> exactly as they do
/// on a service. A decorator that does not apply is never invoked, so the service resolves
/// undecorated rather than being wrapped by something that re-tests the environment per call.
/// </param>
/// <param name="Constructor">
/// The decorator's constructor, so the call can be emitted as a literal <c>new</c> rather than left
/// to <c>ActivatorUtilities</c> at run time.
/// </param>
/// <param name="InnerParameterIndex">
/// Which constructor parameter takes the service being wrapped. Every other parameter is resolved
/// from the provider. -1 when the constructor could not be read, which is what
/// <see cref="CanMonomorphise"/> tests.
/// </param>
/// <param name="TypeParametersMatchService">
/// True when the decorator's type parameters are exactly the service's type arguments, in order —
/// <c>Logging&lt;TReq, TRes&gt; : IHandler&lt;TReq, TRes&gt;</c>. Only then can closing the service
/// over a pair of types be turned into closing the decorator over the same pair. A shape that
/// reorders or reuses them is refused rather than guessed at.
/// </param>
public record DecoratorModel(
    ITypeDefinition ServiceType,
    ITypeDefinition DecoratorType,
    int Order,
    ITypeDefinition? Realm,
    IReadOnlyList<EnvironmentConditionModel>? Conditions = null,
    ConstructorInfoModel? Constructor = null,
    int InnerParameterIndex = -1,
    bool TypeParametersMatchService = true,

    /// <summary>
    /// The one implementation this decorator wraps, or null to wrap every registration of the
    /// service — which is the default and what a decorator declared against an interface means.
    /// </summary>
    ITypeDefinition? Implementation = null,

    /// <summary>
    /// Where the decorator was declared, so DM0007 and DM0013 can point at it rather than at the
    /// project. Null for a decorator declared through [Decorate] on a module, which names two types
    /// and has no declaration of its own to point at.
    /// </summary>
    LocationModel? Location = null) {

    /// <summary>
    /// Whether the decorator can be constructed by generated code.
    /// </summary>
    /// <remarks>
    /// The alternative is a run-time <c>ActivatorUtilities.CreateInstance</c> over a
    /// <see cref="Type"/>, which is exactly what a published Native AOT build cannot rely on. When
    /// this is false the generator reports rather than emitting something that works under a JIT and
    /// fails when published.
    /// </remarks>
    public bool CanMonomorphise =>
        Constructor != null && InnerParameterIndex >= 0 && TypeParametersMatchService;

    /// <summary>
    /// Whether this decorator is generic, and therefore applies to closed constructions of an open
    /// generic service rather than to one named service type.
    /// </summary>
    public bool IsOpenGeneric =>
        DecoratorType is GenericTypeDefinition { TypeArguments.Count: > 0 };

    /// <summary>
    /// Whether the decorated service is still the unbound form, <c>IHandler&lt;&gt;</c>.
    /// </summary>
    /// <remarks>
    /// A generic decorator carries the unbound service until it is expanded against the registrations
    /// that close it. One that reaches emission still unbound had nothing to expand against, and must
    /// take the reflective path: an unbound name is not a legal type argument, so emitting
    /// <c>Decorate&lt;IHandler&lt;&gt;&gt;</c> is CS7003 in generated code — which is the failure
    /// mode this generator is built never to produce.
    /// </remarks>
    public bool HasUnboundServiceType =>
        ServiceType is GenericTypeDefinition generic &&
        generic.TypeArguments.Any(argument => string.IsNullOrEmpty(argument.Name));

    /// <summary>
    /// Sentinel for a syntax node that carried the attribute but produced no usable model, matching
    /// how <see cref="ServiceModel.Ignore"/> is used.
    /// </summary>
    public static readonly DecoratorModel Ignore = new(
        TypeDefinition.Get("", "Ignore"),
        TypeDefinition.Get("", "Ignore"),
        0,
        null);

    public bool IsIgnored => ReferenceEquals(this, Ignore);
}

/// <summary>
/// Equality for the incremental pipeline. Every field affects generated output, so all of them are
/// compared; missing one would serve stale output after an edit to it.
/// </summary>
public class DecoratorModelComparer : IEqualityComparer<DecoratorModel> {

    public bool Equals(DecoratorModel? x, DecoratorModel? y) {
        if (ReferenceEquals(x, y)) {
            return true;
        }

        if (x is null || y is null) {
            return false;
        }

        return x.Order == y.Order &&
               x.ServiceType.Equals(y.ServiceType) &&
               x.DecoratorType.Equals(y.DecoratorType) &&
               Equals(x.Realm, y.Realm) &&
               // Decides which registration is wrapped, so leaving it out would serve the previous
               // emission when only Implementation changed.
               Equals(x.Implementation, y.Implementation) &&
               x.InnerParameterIndex == y.InnerParameterIndex &&
               x.TypeParametersMatchService == y.TypeParametersMatchService &&
               Equals(x.Constructor, y.Constructor) &&
               ConditionsEqual(x.Conditions, y.Conditions);
    }

    // Structural rather than by reference: two runs build separate lists, so comparing references
    // would miss the cache on every keystroke and re-emit every decorator.
    private static bool ConditionsEqual(
        IReadOnlyList<EnvironmentConditionModel>? x,
        IReadOnlyList<EnvironmentConditionModel>? y) =>
        (x?.Count ?? 0) == 0 && (y?.Count ?? 0) == 0 || ModelEquality.ListEquals(x, y);

    public int GetHashCode(DecoratorModel obj) {
        unchecked {
            var hash = obj.ServiceType.GetHashCode();
            hash = hash * 31 + obj.DecoratorType.GetHashCode();
            hash = hash * 31 + obj.Order;
            hash = hash * 31 + (obj.Realm?.GetHashCode() ?? 0);
            hash = hash * 31 + (obj.Implementation?.GetHashCode() ?? 0);
            hash = hash * 31 + obj.InnerParameterIndex;
            hash = hash * 31 + (obj.Constructor?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Conditions);

            return hash;
        }
    }
}

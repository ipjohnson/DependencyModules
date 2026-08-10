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
public record DecoratorModel(
    ITypeDefinition ServiceType,
    ITypeDefinition DecoratorType,
    int Order,
    ITypeDefinition? Realm,
    IReadOnlyList<EnvironmentConditionModel>? Conditions = null) {

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
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Conditions);

            return hash;
        }
    }
}

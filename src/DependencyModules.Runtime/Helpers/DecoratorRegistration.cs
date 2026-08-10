using DependencyModules.Runtime.Interfaces;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// A decorator registration together with the order in which it should be applied.
/// </summary>
/// <remarks>
/// Order is compared across every module in an <c>AddModule(s)</c> call, not just within the module
/// that declared it. A pipeline assembled from several packages would otherwise nest by module
/// discovery order rather than by intent.
/// </remarks>
/// <param name="order">
/// Lower values are applied first and therefore sit closer to the implementation; higher values wrap
/// them. By convention framework packages use 0-999 and application code uses 1000 and above, so an
/// application's decorators wrap those contributed by the libraries it consumes.
/// </param>
/// <param name="registryFunc">
/// The function that rewrites registrations in the collection, receiving the environment any
/// condition on the decorator is evaluated against.
/// </param>
public readonly struct DecoratorRegistration(int order, EnvironmentRegistryFunc registryFunc) {
    /// <summary>
    /// A decorator with no environment condition.
    /// </summary>
    /// <remarks>
    /// Adapted to the environment-taking form rather than stored separately, so conditional and
    /// unconditional decorators sort against each other on <see cref="Order"/> alone. Keeping them
    /// apart would have let a condition change where a decorator sits in the nesting.
    /// </remarks>
    public DecoratorRegistration(int order, RegistryFunc registryFunc)
        : this(order, (serviceCollection, _) => registryFunc(serviceCollection)) { }

    /// <summary>
    /// Order in which this decorator is applied relative to all others.
    /// </summary>
    public int Order { get; } = order;

    /// <summary>
    /// The function that performs the decoration.
    /// </summary>
    public EnvironmentRegistryFunc RegistryFunc { get; } = registryFunc;
}

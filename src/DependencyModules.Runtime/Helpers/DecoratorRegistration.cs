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
/// <param name="registryFunc">The function that rewrites registrations in the collection.</param>
public readonly struct DecoratorRegistration(int order, RegistryFunc registryFunc) {
    /// <summary>
    /// Order in which this decorator is applied relative to all others.
    /// </summary>
    public int Order { get; } = order;

    /// <summary>
    /// The function that performs the decoration.
    /// </summary>
    public RegistryFunc RegistryFunc { get; } = registryFunc;
}

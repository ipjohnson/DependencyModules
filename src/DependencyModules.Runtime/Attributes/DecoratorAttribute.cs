namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Marks a class as a decorator. The generator rewrites the registrations of the decorated service
/// so that resolving it returns this class wrapping the original implementation.
/// </summary>
/// <remarks>
/// The decorated service is inferred from the constructor: the parameter whose type the decorator
/// also implements is the one being wrapped. Set <see cref="Service"/> when a decorator implements
/// more than one candidate interface.
///
/// <code>
/// [Decorator(Order = 100)]
/// public class CachingRepository(IRepository inner, IMemoryCache cache) : IRepository {
///     public Item Get(int id) => cache.GetOrCreate(id, _ => inner.Get(id))!;
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DecoratorAttribute : Attribute {
    /// <summary>
    /// Controls how decorators nest. Lower values are applied first and therefore sit closer to the
    /// implementation; higher values wrap them.
    /// </summary>
    /// <remarks>
    /// Compared across every module in an <c>AddModule(s)</c> call, not only within the declaring
    /// module. By convention framework packages use 0-999 and application code uses 1000 and above,
    /// so an application's decorators wrap those contributed by the libraries it consumes.
    /// </remarks>
    public int Order { get; set; }

    /// <summary>
    /// The service being decorated. Only needed when it cannot be inferred, which happens when the
    /// decorator implements more than one interface it also accepts as a constructor parameter.
    /// </summary>
    public Type? Service { get; set; }

    /// <summary>
    /// Restricts the decorator to a single module, matching the Realm property on the service
    /// registration attributes.
    /// </summary>
    public Type? Realm { get; set; }

    /// <summary>
    /// Restricts the decorator to one implementation of the service, rather than every registration
    /// of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wrapping every registration is the default and is what separates a decorator from an
    /// interceptor: the decorator is written against the interface and does not care which class is
    /// behind it. Naming an implementation is for the case where that is not true — several
    /// implementations of one interface, and the behaviour belongs to one of them.
    /// </para>
    /// <para>
    /// A registration built from a factory cannot say what implementation it came from, so naming
    /// one here is not honoured for those — the same limit interception has, and the reason an
    /// intercepted service keeps its <c>typeof</c> registration under
    /// <c>DependencyModules_GenerateFactories</c>.
    /// </para>
    /// </remarks>
    public Type? Implementation { get; set; }
}

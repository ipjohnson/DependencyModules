namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Declares a decorator on a module rather than on the decorator class itself.
/// </summary>
/// <remarks>
/// Use this when the decorated service, the decorator, or both come from an assembly you do not
/// control, so there is nowhere to put a <see cref="DecoratorAttribute"/>.
///
/// <code>
/// [DependencyModule]
/// [Decorate(typeof(IRepository), typeof(CachingRepository), Order = 100)]
/// public partial class DataModule;
/// </code>
/// </remarks>
/// <param name="service">The service being decorated. May be an open generic.</param>
/// <param name="decorator">The decorator, which must implement <paramref name="service"/>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class DecorateAttribute(Type service, Type decorator) : Attribute {
    /// <summary>
    /// The service being decorated.
    /// </summary>
    public Type Service { get; } = service;

    /// <summary>
    /// The decorator wrapping it.
    /// </summary>
    public Type Decorator { get; } = decorator;

    /// <summary>
    /// Controls how decorators nest. See <see cref="DecoratorAttribute.Order"/>.
    /// </summary>
    public int Order { get; set; }
}

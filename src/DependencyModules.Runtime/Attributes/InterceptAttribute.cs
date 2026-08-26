namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Wraps a service so every call through its interface passes through the given interceptors.
/// </summary>
/// <remarks>
/// The wrapper implementing the interface is generated, which is what allows one interceptor to
/// apply to unrelated services. Only calls made through the interface are intercepted; a call the
/// implementation makes to itself does not pass through the wrapper.
///
/// <code>
/// [SingletonService]
/// [Intercept(typeof(LoggingInterceptor))]
/// public class Repository : IRepository { }
/// </code>
/// </remarks>
/// <param name="interceptors">
/// The interceptor types to apply, in order. Each is resolved from the container.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class InterceptAttribute(params Type[] interceptors) : Attribute {
    /// <summary>
    /// The interceptors applied to the service.
    /// </summary>
    public Type[] Interceptors { get; } = interceptors;

    /// <summary>
    /// The interface to intercept. Only needed when the service implements more than one.
    /// </summary>
    public Type? Service { get; set; }

    /// <summary>
    /// Controls nesting relative to decorators and other interceptors. See
    /// <see cref="DecoratorAttribute.Order"/>.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Restricts the interception to a single module, matching the Realm property on the service
    /// registration attributes and on <see cref="DecoratorAttribute.Realm"/>.
    /// </summary>
    /// <remarks>
    /// Without a realm the interception belongs to every module that is not realm-only, which is how
    /// services and decorators already behave. An <c>OnlyRealm</c> module previously received every
    /// interception in the compilation regardless — including ones naming no realm at all — so a
    /// module built to be isolated wrapped services it had never heard of.
    /// </remarks>
    public Type? Realm { get; set; }
}

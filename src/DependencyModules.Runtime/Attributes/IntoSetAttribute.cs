namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Adds the class to the set of <typeparamref name="T"/>. A class gets the set as
/// <c>IReadOnlySet&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// The class is not registered as a service. The set builds its items when it is first read, and
/// gets the constructor parameters from the service provider.
/// </remarks>
/// <typeparam name="T">The type of the items of the set.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IntoSetAttribute<T> : Attribute
{
    /// <summary>
    /// The module that adds the class to the set. If it is not set, each module of the project
    /// that is not realm-only adds the class.
    /// </summary>
    public Type? Realm { get; set; }
}

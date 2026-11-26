namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Adds the class to the map of <typeparamref name="TKey"/> to <typeparamref name="TValue"/>.
/// A class gets the map as <c>IReadOnlyDictionary&lt;TKey, TValue&gt;</c>.
/// </summary>
/// <remarks>
/// The class is not registered as a service. The map builds the class when its key is first read,
/// and gets the constructor parameters from the service provider.
/// </remarks>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the items of the map.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IntoMapAttribute<TKey, TValue> : Attribute
    where TKey : notnull
{
    /// <param name="key">The key of the class in the map.</param>
    public IntoMapAttribute(TKey key)
    {
        Key = key;
    }

    /// <summary>
    /// The key of the class in the map.
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    /// The module that adds the class to the map. If it is not set, each module of the project
    /// that is not realm-only adds the class.
    /// </summary>
    public Type? Realm { get; set; }
}

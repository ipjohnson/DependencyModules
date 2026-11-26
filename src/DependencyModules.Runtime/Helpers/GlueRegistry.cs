using System.ComponentModel;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// Delegate representing a function that adds the collection items and the <c>Func</c>
/// registrations of a module.
/// </summary>
/// <param name="glue">The registry of the service collection that the module loads into.</param>
/// <param name="environment">The environment conditions are evaluated against. Never null.</param>
public delegate void GlueFunc(GlueRegistry glue, IModuleEnvironment environment);

/// <summary>
/// Holds the collection items and the <c>Func</c> registrations that the loaded modules add to one
/// service collection. Generated code calls it. It is not intended to be called by developers.
/// </summary>
/// <remarks>
/// The registry is in the service collection as a singleton instance, so each service collection
/// has its own items. A second <c>AddModule</c> call on the same collection adds to the same
/// maps, sets, and lists.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GlueRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<Type, IGlueCollection> _collections = new();
    private List<ServiceDescriptor>? _funcs;

    internal GlueRegistry() { }

    /// <summary>
    /// Adds an item to the map of <typeparamref name="TKey"/> to <typeparamref name="TValue"/>.
    /// </summary>
    /// <param name="key">The key of the item.</param>
    /// <param name="implementationType">The class that the factory builds.</param>
    /// <param name="factory">Builds the item.</param>
    public void AddMapItem<TKey, TValue>(
        TKey key,
        Type implementationType,
        Func<IServiceProvider, TValue> factory
    )
        where TKey : notnull
    {
        lock (_lock)
        {
            Collection<GlueMapBuilder<TKey, TValue>>(
                    typeof(IReadOnlyDictionary<TKey, TValue>),
                    implementationType
                )
                .Add(key, implementationType, factory);
        }
    }

    /// <summary>
    /// Adds an item to the set of <typeparamref name="T"/>.
    /// </summary>
    /// <param name="implementationType">The class that the factory builds.</param>
    /// <param name="factory">Builds the item.</param>
    public void AddSetItem<T>(Type implementationType, Func<IServiceProvider, T> factory)
    {
        lock (_lock)
        {
            Collection<GlueSetBuilder<T>>(typeof(IReadOnlySet<T>), implementationType)
                .Add(implementationType, factory);
        }
    }

    /// <summary>
    /// Adds an item to the list of <typeparamref name="T"/>, with an order that comes from types.
    /// </summary>
    /// <param name="implementationType">The class that the factory builds.</param>
    /// <param name="dependsOn">The item comes after each item that is assignable to one of these types.</param>
    /// <param name="factory">Builds the item.</param>
    public void AddListItem<T>(
        Type implementationType,
        Type[] dependsOn,
        Func<IServiceProvider, T> factory
    )
    {
        lock (_lock)
        {
            Collection<GlueTypeListBuilder<T>>(typeof(IReadOnlyList<T>), implementationType)
                .Add(implementationType, dependsOn, factory);
        }
    }

    /// <summary>
    /// Adds an item to the list of <typeparamref name="T"/>, with an order that comes from values.
    /// </summary>
    /// <param name="value">The order value of the item.</param>
    /// <param name="dependsOn">The item comes after each item that has one of these values.</param>
    /// <param name="implementationType">The class that the factory builds.</param>
    /// <param name="factory">Builds the item.</param>
    public void AddListItem<T, TOrder>(
        TOrder value,
        TOrder[] dependsOn,
        Type implementationType,
        Func<IServiceProvider, T> factory
    )
    {
        lock (_lock)
        {
            Collection<GlueValueListBuilder<T, TOrder>>(
                    typeof(IReadOnlyList<T>),
                    implementationType
                )
                .Add(value, dependsOn, implementationType, factory);
        }
    }

    /// <summary>
    /// Adds a registration of the delegate type <typeparamref name="TFunc"/>. The registration is
    /// made only if the service collection has none when the modules complete their load.
    /// </summary>
    /// <param name="factory">Makes the delegate for a service provider.</param>
    public void AddFunc<TFunc>(Func<IServiceProvider, TFunc> factory)
        where TFunc : Delegate
    {
        lock (_lock)
        {
            (_funcs ??= []).Add(
                new ServiceDescriptor(typeof(TFunc), factory, ServiceLifetime.Transient)
            );
        }
    }

    /// <summary>
    /// The registry in the collection, or null.
    /// </summary>
    internal static GlueRegistry? Find(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (
                services[i].ServiceType == typeof(GlueRegistry)
                && services[i].ImplementationInstance is GlueRegistry registry
            )
            {
                return registry;
            }
        }

        return null;
    }

    internal static GlueRegistry FindOrAdd(IServiceCollection services)
    {
        var registry = Find(services);

        if (registry == null)
        {
            registry = new GlueRegistry();
            services.Add(new ServiceDescriptor(typeof(GlueRegistry), registry));
        }

        return registry;
    }

    /// <summary>
    /// Checks the collections and registers what the service collection does not have.
    /// </summary>
    /// <remarks>
    /// Runs after every other phase of the load, and uses <c>TryAdd</c>. A registration of a
    /// collection type or a delegate type that a module or the application made is kept.
    /// </remarks>
    internal void Complete(IServiceCollection services)
    {
        lock (_lock)
        {
            foreach (var collection in _collections.Values)
            {
                collection.Validate();
                services.TryAdd(collection.Descriptor);
            }

            if (_funcs == null)
            {
                return;
            }

            foreach (var func in _funcs)
            {
                services.TryAdd(func);
            }

            _funcs = null;
        }
    }

    private TBuilder Collection<TBuilder>(Type serviceType, Type implementationType)
        where TBuilder : class, IGlueCollection, new()
    {
        if (!_collections.TryGetValue(serviceType, out var collection))
        {
            collection = new TBuilder();
            _collections[serviceType] = collection;
        }

        // Only a list can get here with the wrong builder: the two IntoList attributes give the
        // same IReadOnlyList<T>.
        return collection as TBuilder
            ?? throw new InvalidOperationException(
                $"{GlueNames.Of(serviceType)} has items with more than one order scheme. "
                    + $"{GlueNames.Of(implementationType)} uses {new TBuilder().Scheme}, "
                    + $"and an item before it uses {collection.Scheme}. "
                    + "Use one IntoList attribute, with one order type, for all items of a list."
            );
    }
}

internal interface IGlueCollection
{
    /// <summary>The attribute that gives items to the collection, for messages.</summary>
    string Scheme { get; }

    /// <summary>The registration of the collection.</summary>
    ServiceDescriptor Descriptor { get; }

    /// <summary>Throws when the items cannot be put in order.</summary>
    void Validate();
}

internal static class GlueNames
{
    /// <summary>
    /// The name of a type as it is written in C#, with its namespace.
    /// </summary>
    public static string Of(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var name = type.Name;
        var tick = name.IndexOf('`');

        if (tick >= 0)
        {
            name = name.Substring(0, tick);
        }

        var arguments = string.Join(", ", type.GetGenericArguments().Select(Of));

        return $"{type.Namespace}.{name}<{arguments}>";
    }
}

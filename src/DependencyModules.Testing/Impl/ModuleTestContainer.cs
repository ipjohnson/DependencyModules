using System.Reflection;
using System.Runtime.ExceptionServices;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// A built test container, and every container its <see cref="ITestContainerSource"/> built.
/// </summary>
/// <remarks>
/// <see cref="ModuleTestComposition.BuildAsync"/> and <see cref="ModuleTestComposition.BuildScopeAsync"/>
/// create it. <see cref="DisposeAsync"/> disposes every container it tracks, the last one built
/// first.
/// </remarks>
public sealed class ModuleTestContainer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<IServiceProvider> _providers = [];
    private readonly Dictionary<(Type, object?), object?> _sharedInstances = new();
    private readonly TestParameterResolver? _resolver;
    private readonly IReadOnlyList<Attribute> _scopeAttributes;
    private IServiceProvider? _services;

    internal ModuleTestContainer(
        TestParameterResolver? resolver,
        IReadOnlyList<Attribute> scopeAttributes
    )
    {
        _resolver = resolver;
        _scopeAttributes = scopeAttributes;
    }

    /// <summary>
    /// The test's container.
    /// </summary>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("The container is not built yet.");

    /// <summary>
    /// The arguments for the whole method, as the xUnit and NUnit packages resolve them.
    /// </summary>
    /// <param name="data">
    /// The values of a data row, for the leading parameters. Empty for a test with no data.
    /// </param>
    /// <returns>One argument per parameter, in declaration order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The container came from <see cref="ModuleTestComposition.BuildScopeAsync"/>, which has no test
    /// method.
    /// </exception>
    public Task<object?[]> ResolveArgumentsAsync(object?[] data) =>
        Resolver().ResolveArgumentsAsync(Services, data);

    /// <summary>
    /// The value of the <see cref="ITestParameterValueProvider"/> attributes on the parameter, or
    /// null when none answers.
    /// </summary>
    /// <remarks>
    /// Null for a parameter that a <see cref="TestServiceOverride"/> supplies, because its
    /// attributes do not run.
    /// </remarks>
    /// <param name="parameter">A parameter of the test method.</param>
    /// <exception cref="InvalidOperationException">
    /// The container came from <see cref="ModuleTestComposition.BuildScopeAsync"/>, which has no test
    /// method.
    /// </exception>
    public Task<object?> GetParameterValueAsync(ParameterInfo parameter) =>
        Resolver().GetParameterValueAsync(parameter, Services);

    /// <summary>
    /// <see cref="IServiceProvider"/>, a keyed service, a registered service, or an unregistered
    /// non-abstract class built with its <c>[InjectValues]</c>. Null when none applies.
    /// </summary>
    /// <remarks>
    /// A parameter with <see cref="FromKeyedServicesAttribute"/> gets the keyed service only.
    /// </remarks>
    /// <param name="parameter">The parameter to resolve.</param>
    public object? Resolve(ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(IServiceProvider))
        {
            return Services;
        }

        return TestParameterResolver.ResolveFromContainer(parameter, Services, constructAny: false);
    }

    /// <summary>
    /// A keyed or unkeyed registered service, or null.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The service key, or null for the unkeyed service.</param>
    public object? Resolve(Type serviceType, object? serviceKey)
    {
        if (serviceKey == null)
        {
            return Services.GetService(serviceType);
        }

        return Services is IKeyedServiceProvider keyed
            ? keyed.GetKeyedService(serviceType, serviceKey)
            : null;
    }

    /// <summary>
    /// Disposes every container that this container tracks, the last one built first.
    /// </summary>
    /// <remarks>
    /// Each container is disposed when another one throws. One exception is rethrown as it is, and
    /// more than one are thrown as an <see cref="AggregateException"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        IServiceProvider[] providers;

        lock (_gate)
        {
            providers = _providers.ToArray();
            _providers.Clear();
        }

        List<Exception>? exceptions = null;

        for (var i = providers.Length - 1; i >= 0; i--)
        {
            try
            {
                switch (providers[i])
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        if (exceptions is [var single])
        {
            ExceptionDispatchInfo.Capture(single).Throw();
        }

        if (exceptions != null)
        {
            throw new AggregateException(exceptions);
        }
    }

    internal void SetServices(IServiceProvider services)
    {
        Track(services);
        _services = services;
    }

    internal void Track(IServiceProvider provider)
    {
        lock (_gate)
        {
            _providers.Add(provider);
        }
    }

    /// <summary>
    /// Registers into a child's collection this container's instances of the services that the
    /// child shares with it.
    /// </summary>
    /// <remarks>
    /// The shared services are the type of each parameter with <see cref="SharedAttribute"/>, and the
    /// <see cref="TestExportAttribute.Service"/> of each <c>[TestExport(Shared = true)]</c> in this
    /// container's scope attributes. A type that an
    /// <see cref="ISharedTestRegistration.IsolatedServices"/> names is not shared. Each instance is
    /// resolved once and kept, so every child gets the same one.
    /// </remarks>
    internal void RegisterSharedServices(IServiceCollection services, ITestMethodContext child)
    {
        var isolated = new HashSet<Type> { typeof(IServiceProvider) };

        foreach (
            var registration in child
                .Attributes.Concat(_scopeAttributes)
                .OfType<ISharedTestRegistration>()
        )
        {
            isolated.UnionWith(registration.IsolatedServices(child.Method));
        }

        var shared = new List<(Type Type, object? Key)>();

        foreach (var parameter in child.Method.GetParameters())
        {
            if (parameter.IsDefined(typeof(SharedAttribute), inherit: false))
            {
                shared.Add(
                    (
                        parameter.ParameterType,
                        parameter.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key
                    )
                );
            }
        }

        foreach (var export in _scopeAttributes.OfType<TestExportAttribute>())
        {
            if (export.Shared)
            {
                shared.Add((export.Service, null));
            }
        }

        foreach (var (type, key) in shared.Distinct())
        {
            if (isolated.Contains(type) || SharedInstance(type, key) is not { } instance)
            {
                continue;
            }

            services.Add(
                key == null
                    ? new ServiceDescriptor(type, instance)
                    : new ServiceDescriptor(type, key, instance)
            );
        }
    }

    private object? SharedInstance(Type type, object? key)
    {
        lock (_gate)
        {
            if (!_sharedInstances.TryGetValue((type, key), out var instance))
            {
                instance = Resolve(type, key);
                _sharedInstances[(type, key)] = instance;
            }

            return instance;
        }
    }

    private TestParameterResolver Resolver() =>
        _resolver
        ?? throw new InvalidOperationException(
            $"This container was built by {nameof(ModuleTestComposition.BuildScopeAsync)} for a class "
                + "or an assembly. It has no test method, so it has no parameters to supply."
        );
}

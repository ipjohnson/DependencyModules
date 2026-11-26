using System.Collections;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// The container of a class or assembly scope.
/// </summary>
/// <remarks>
/// <para>
/// Each test gets a child built from the test's own attributes and overrides, the same as a test
/// container. A child that is a scope of this provider could not return the test's mocks to the
/// services it creates, because a scope adds no registrations.
/// </para>
/// <para>
/// What the tests share is what they ask to share, which is how DependencyModules.Testing reads
/// <see cref="SharedAttribute"/> and <c>[TestExport(Shared = true)]</c>: the type of a
/// <c>[Shared]</c> parameter, and the services that an <see cref="ISharedTestRegistration"/> in this
/// scope names. This provider creates each of them once and owns it. A child registers it as an
/// instance, so the child does not dispose it.
/// </para>
/// </remarks>
internal sealed class SharedModuleContainer : ITestContainer
{
    /// <summary>
    /// The services a child supplies itself and never takes from here.
    /// </summary>
    private static readonly Type[] NeverShared =
    [
        typeof(IServiceProvider),
        typeof(ICurrentTestProvider),
        typeof(ITestOutput),
    ];

    private readonly IServiceProvider _provider;
    private readonly Func<IDependencyModule[]> _modules;
    private readonly IReadOnlyList<Type> _exports;
    private readonly Dictionary<Type, object[]> _instances = new();
    private readonly List<object> _created = [];

    private SharedModuleContainer(
        IServiceProvider provider,
        Func<IDependencyModule[]> modules,
        IReadOnlyList<Type> exports
    )
    {
        _provider = provider;
        _modules = modules;
        _exports = exports;
    }

    public static SharedModuleContainer Build(
        TestContainerContext context,
        Func<IDependencyModule[]> modules
    )
    {
        var provider = ModuleContainers.BuildSharedProvider(context, modules());

        var exports = context
            .Attributes.OfType<ISharedTestRegistration>()
            .Where(registration => registration.Shared)
            .SelectMany(registration => registration.SharedServices)
            .Distinct()
            .ToArray();

        return new SharedModuleContainer(provider, modules, exports);
    }

    public IServiceProvider Services => _provider;

    public bool TryResolve(TestParameter parameter, out object? value) =>
        ParameterResolution.TryResolve(_provider, parameter, null, Track, out value);

    public ValueTask<ITestContainer> CreateChildAsync(TestContainerContext context) =>
        ModuleContainers.BuildTestContainerAsync(context, _modules, this);

    /// <summary>
    /// Replaces the registrations of every shared service in a child's collection with the
    /// instances from this container.
    /// </summary>
    /// <remarks>
    /// Before the overrides, so a test's <c>[Mock]</c> replaces a shared service of the same type.
    /// A keyed registration is left as it is: the instances come from <c>IEnumerable&lt;T&gt;</c>,
    /// which holds no keyed registration.
    /// </remarks>
    public void AddSharedInstances(IServiceCollection services, TestContainerContext context)
    {
        foreach (var serviceType in SharedServices(context))
        {
            var instances = InstancesOf(serviceType);

            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == serviceType && !services[i].IsKeyedService)
                {
                    services.RemoveAt(i);
                }
            }

            foreach (var instance in instances)
            {
                services.Add(new ServiceDescriptor(serviceType, instance));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        object[] created;

        lock (_created)
        {
            created = _created.ToArray();
            _created.Clear();
        }

        foreach (var instance in created)
        {
            await ModuleContainers.DisposeAsync(instance);
        }

        await ModuleContainers.DisposeAsync(_provider);
    }

    private IEnumerable<Type> SharedServices(TestContainerContext context)
    {
        var shared = new HashSet<Type>(_exports);

        foreach (var parameter in context.Parameters)
        {
            foreach (var registration in parameter.Attributes.OfType<ISharedTestRegistration>())
            {
                if (!registration.Shared)
                {
                    continue;
                }

                if (registration is SharedAttribute)
                {
                    shared.Add(parameter.Type);
                }

                shared.UnionWith(registration.SharedServices);
            }
        }

        shared.ExceptWith(NeverShared);

        return shared;
    }

    /// <remarks>
    /// Under a lock, so two tests that start at the same time get the same instance. Resolved
    /// through <c>IEnumerable&lt;T&gt;</c>, so a type with several registrations keeps each of
    /// them in its order.
    /// </remarks>
    private object[] InstancesOf(Type serviceType)
    {
        lock (_instances)
        {
            if (_instances.TryGetValue(serviceType, out var instances))
            {
                return instances;
            }

            if (
                serviceType.IsGenericTypeDefinition
                || serviceType.IsByRef
                || serviceType.IsPointer
                || serviceType.IsValueType
            )
            {
                throw new InvalidOperationException(
                    $"The tests of this scope share {serviceType}, which is not a service type a "
                        + "container can hold one instance of."
                );
            }

            var sequence = typeof(IEnumerable<>).MakeGenericType(serviceType);

            instances = ((IEnumerable)_provider.GetRequiredService(sequence))
                .Cast<object>()
                .Where(instance => instance != null)
                .ToArray();

            if (instances.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The tests of this scope share {serviceType}, but the class or assembly "
                        + "container has no registration for it. Register it with a module or a "
                        + "[TestExport] on the assembly or the class that owns the container."
                );
            }

            _instances[serviceType] = instances;

            return instances;
        }
    }

    private void Track(object instance)
    {
        lock (_created)
        {
            _created.Add(instance);
        }
    }
}

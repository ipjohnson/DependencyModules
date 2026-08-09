using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// Delegate representing a function responsible for registering dependencies
/// into an IServiceCollection.
/// </summary>
/// <param name="serviceCollection">The IServiceCollection to which dependencies will be added.</param>
public delegate void RegistryFunc(IServiceCollection serviceCollection);

/// <summary>
/// Delegate representing a function responsible for registering dependencies into an
/// IServiceCollection, with the environment those registrations may be conditional on.
/// </summary>
/// <remarks>
/// Generated code uses this form only when a module declares an environment condition. Everything
/// registered through <see cref="RegistryFunc"/> is adapted to it, so both kinds keep their
/// declaration order relative to each other — which matters, because the container resolves a
/// single service from the last matching descriptor.
/// </remarks>
/// <param name="serviceCollection">The IServiceCollection to which dependencies will be added.</param>
/// <param name="environment">The environment conditions are evaluated against. Never null.</param>
public delegate void EnvironmentRegistryFunc(
    IServiceCollection serviceCollection, IModuleEnvironment environment);

/// <summary>
///     Static class used to store dependency registration functions
///     per type
/// </summary>
/// <typeparam name="T"></typeparam>
// ReSharper disable once ClassNeverInstantiated.Global
public class DependencyRegistry<T> {
    // ReSharper disable StaticMemberInGenericType
    private static readonly object SyncLock = new();
    private static readonly List<EnvironmentRegistryFunc> RegistryFuncs = [];
    private static readonly List<DecoratorRegistration> Decorators = [];
    private static readonly List<IDependencyModule> Modules = [];

    /// <summary>
    ///     Add registration func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(RegistryFunc registryFunc) {
        lock (SyncLock) {
            RegistryFuncs.Add((serviceCollection, _) => registryFunc(serviceCollection));
        }

        return 1;
    }

    /// <summary>
    ///     Add registration func whose registrations may depend on the environment
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(EnvironmentRegistryFunc registryFunc) {
        lock (SyncLock) {
            RegistryFuncs.Add(registryFunc);
        }

        return 1;
    }

    /// <summary>
    /// Adding singleton instance, intended to be used as a short cut
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="lifetime"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static int Add<TInstance>(
        Func<IServiceProvider, TInstance> provider,
        ServiceLifetime lifetime = ServiceLifetime.Transient) where TInstance : class {
        lock (SyncLock) {
            RegistryFuncs.Add(
                (registry, _) => registry.Add(
                    new ServiceDescriptor(
                        typeof(TInstance),
                        provider,
                        lifetime
                    )));
        }
        return 1;
    }

    /// <summary>
    /// Add instance of of dependency
    /// </summary>
    /// <param name="implementationType"></param>
    /// <param name="lifetime"></param>
    /// <param name="serviceKey"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static int Add<TInstance>(Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient, object? serviceKey = null) where TInstance : class {
        lock (SyncLock) {
            RegistryFuncs.Add(
                (registry, _) => registry.Add(
                    new ServiceDescriptor(
                        typeof(TInstance),
                        serviceKey,
                        implementationType,
                        lifetime
                    )));
        }
        return 1;
    }

    /// <summary>
    ///      Add decorator func
    /// </summary>
    /// <param name="registryFunc">Function that decorates registrations already in the collection.</param>
    /// <param name="order">
    ///     Controls how decorators nest. Lower values are applied first and therefore sit closer to
    ///     the implementation; higher values wrap them. Decorators sharing an order are applied in
    ///     registration order.
    ///
    ///     By convention, framework packages use 0-999 and application code uses 1000 and above, so
    ///     that an application's decorators wrap those contributed by the libraries it consumes.
    /// </param>
    /// <returns></returns>
    public static int AddDecorator(RegistryFunc registryFunc, int order = 0) {
        lock (SyncLock) {
            Decorators.Add(new DecoratorRegistration(order, registryFunc));
        }

        return 1;
    }

    /// <summary>
    /// Add module
    /// </summary>
    /// <param name="modules"></param>
    /// <returns></returns>
    public static int AddModule(params IDependencyModule[] modules) {
        lock (SyncLock) {
            Modules.AddRange(modules);
        }

        return 1;
    }

    /// <summary>
    ///     Load modules into service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="dependencyModules"></param>
    public static void LoadModules(IServiceCollection serviceCollection, params IDependencyModule[] dependencyModules) {
        var modules = GetAllModules(dependencyModules);
        
        ApplyFeatures(serviceCollection, modules);

        ApplyServices(serviceCollection, modules);
        
        ApplyDecorators(serviceCollection, modules);
    }
    
    /// <summary>
    ///     Apply all registration for a given type to the service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyServices(IServiceCollection serviceCollection) {
        ApplyServices(serviceCollection, ModuleEnvironment.Default);
    }

    /// <summary>
    ///     Apply all registration for a given type to the service collection, evaluating any
    ///     environment conditions against the supplied environment
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="environment"></param>
    public static void ApplyServices(IServiceCollection serviceCollection, IModuleEnvironment environment) {
        EnvironmentRegistryFunc[] snapshot;
        lock (SyncLock) {
            snapshot = RegistryFuncs.ToArray();
        }

        foreach (var registryFunc in snapshot) {
            registryFunc(serviceCollection, environment);
        }
    }

    /// <summary>
    /// Apply all decorators
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection) {
        // OrderBy is a stable sort, so decorators sharing an order keep their registration order
        // rather than nesting arbitrarily.
        foreach (var decorator in GetDecorators().OrderBy(decorator => decorator.Order)) {
            decorator.RegistryFunc(serviceCollection);
        }
    }

    /// <summary>
    /// The decorators registered for this type, unsorted.
    /// </summary>
    /// <remarks>
    /// Generated modules return these from <see cref="IDependencyModule.InternalGetDecorators"/> so
    /// that decorators from every module can be sorted together. Applying each module's decorators
    /// separately would make module discovery order outrank the declared order.
    /// </remarks>
    public static IReadOnlyList<DecoratorRegistration> GetDecorators() {
        lock (SyncLock) {
            return Decorators.ToArray();
        }
    }

    /// <summary>
    /// GetModules that have been registered
    /// </summary>
    /// <param name="modules"></param>
    /// <returns></returns>
    public static IEnumerable<object> GetModules(params object[] modules) {
        List<IDependencyModule> snapshot;
        lock (SyncLock) {
            snapshot = Modules.ToList();
        }

        if (modules.Length == 0) {
            return snapshot;
        }

        if (snapshot.Count == 0) {
            return modules;
        }

        return snapshot.Concat(modules);
    }

    private static void ApplyDecorators(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        // Gathered from every module and sorted together, the same way ApplyFeatures collects and
        // sorts feature applicators. Applying each module's decorators in turn would let module
        // discovery order outrank the declared order, which breaks a pipeline assembled from more
        // than one package.
        var decorators = new List<DecoratorRegistration>();

        for (var i = 0; i < modules.Count; i++) {
            decorators.AddRange(modules[i].InternalGetDecorators());
        }

        if (decorators.Count > 0) {
            foreach (var decorator in decorators.OrderBy(decorator => decorator.Order)) {
                decorator.RegistryFunc(serviceCollection);
            }
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];

            // Retained for hand-written modules that decorate directly. Generated modules use
            // InternalGetDecorators so their decorators take part in the global ordering.
            module.InternalApplyDecorators(serviceCollection);

            // Mirrors how ApplyServices invokes ConfigureServices. Runs last, so the manual escape
            // hatch sees every declared decorator already in place.
            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureDecorators(serviceCollection);
            }
        }
    }

    private static void ApplyServices(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        // Always looked for now. Attribute conditions live on generated modules, which do not
        // implement IEnvironmentServiceCollectionConfiguration, so the old "only if some module
        // asked for it" gate would have missed them. It costs one scan of the collection per
        // AddModules call.
        // One environment for the whole call, and never null. Handing attribute conditions the
        // process default while handing IEnvironmentServiceCollectionConfiguration a null would let
        // one module see two different answers to "what environment is this" — the conditions
        // evaluating against Production while its own ConfigureServices was told there is no
        // environment at all. An application with no environment says so with ModuleEnvironment.None.
        // Nothing to apply means nothing to decide, so the collection is left exactly as it was
        // rather than picking up an environment nobody asked for.
        if (modules.Count == 0) {
            return;
        }

        var environment = FindModuleEnvironment(serviceCollection);

        if (environment == null) {
            RefuseUnusableEnvironment(serviceCollection);

            environment = ModuleEnvironment.Default;

            // Registered, not just used. Otherwise conditions would be decided by an environment
            // that GetRequiredService<IModuleEnvironment>() then throws for, which is the same
            // inconsistency one layer out. Only when nothing supplied one, so an application's own
            // environment is never displaced.
            serviceCollection.AddSingleton(environment);
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyServices(serviceCollection, environment);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }

            if (module is IEnvironmentServiceCollectionConfiguration environmentConfigure) {
                environmentConfigure.ConfigureServices(serviceCollection, environment);
            }
        }
    }

    /// <summary>
    /// Refuses an <see cref="IModuleEnvironment"/> registered in a form that cannot decide
    /// registrations.
    /// </summary>
    /// <remarks>
    /// The environment is read while the collection is still being populated, so there is no
    /// provider to construct it from and only an instance can be used. Registered by type or by
    /// factory it was previously ignored without a word, the process default was used instead, and
    /// the registration that was ignored got shadowed by the one added in its place — a service
    /// gated on "Development" quietly took its production branch.
    /// </remarks>
    private static void RefuseUnusableEnvironment(IServiceCollection serviceCollection) {
        for (var i = serviceCollection.Count - 1; i >= 0; i--) {
            if (serviceCollection[i].ServiceType != typeof(IModuleEnvironment)) {
                continue;
            }

            throw new InvalidOperationException(
                "An IModuleEnvironment is registered, but not as a singleton instance, so it cannot " +
                "be used. The environment decides which services are registered, which happens " +
                "while the service collection is being populated and before any provider exists to " +
                "construct it from. Register the instance directly with " +
                "AddSingleton<IModuleEnvironment>(new MyEnvironment()), or pass it to " +
                "AddModules(environment, modules).");
        }
    }

    private static IModuleEnvironment? FindModuleEnvironment(IServiceCollection serviceCollection) {
        for (var i = serviceCollection.Count - 1; i >= 0; i--) {
            var descriptor = serviceCollection[i];
            if (descriptor.ServiceType == typeof(IModuleEnvironment) &&
                descriptor is {
                    Lifetime: ServiceLifetime.Singleton, 
                    ImplementationInstance: IModuleEnvironment environment
                }) {
                return environment;
            }
        }

        return null;
    }

    private static void ApplyFeatures(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        var features = new List<IFeatureApplicator>();

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            
            if (module is IDependencyModuleApplicatorProvider provider) {
                foreach (var featureApplicator in provider.FeatureApplicators()) {
                    features.Add(featureApplicator);
                }
            }
        }
        
        if (features.Count > 0) {
            features.Sort((x, y) => x.Order.CompareTo(y.Order));

            for (var i = 0; i < features.Count; i++) {
                var feature = features[i];
                feature.Apply(serviceCollection, modules);
            }
        }
    }


    private static IReadOnlyList<IDependencyModule> GetAllModules(IDependencyModule[] dependencyModules) {
        var list = new List<IDependencyModule>();

        foreach (var dependencyModule in dependencyModules) {
            InternalGetModules(dependencyModule, list);
        }

        return list;
    }

    
    private static void InternalGetModules(IDependencyModule dependencyModule, List<IDependencyModule> allDependencyModules) {
        if (!dependencyModule.LoadModule || 
            allDependencyModules.Contains(dependencyModule)) {
            return;
        }

        allDependencyModules.Insert(0, dependencyModule);

        foreach (var dependencyObject in dependencyModule.InternalGetModules()) {
            if (dependencyObject is IDependencyModuleProvider moduleProvider) {
                var dep = moduleProvider.GetModule();
                InternalGetModules(dep, allDependencyModules);
            }
            else if (dependencyObject is IDependencyModule module) {
                InternalGetModules(module, allDependencyModules);
            }
        }
        
        foreach (var module in dependencyModule.GetModules()) {
            InternalGetModules(module, allDependencyModules);
        }
    }
}
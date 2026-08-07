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
///     Static class used to store dependency registration functions
///     per type
/// </summary>
/// <typeparam name="T"></typeparam>
// ReSharper disable once ClassNeverInstantiated.Global
public class DependencyRegistry<T> {
    // ReSharper disable StaticMemberInGenericType
    private static readonly object SyncLock = new();
    private static readonly List<RegistryFunc> RegistryFuncs = [];
    private static readonly List<DecoratorRegistration> Decorators = [];
    private static readonly List<IDependencyModule> Modules = [];

    /// <summary>
    ///     Add registration func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(RegistryFunc registryFunc) {
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
                registry => registry.Add(
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
                registry => registry.Add(
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
        RegistryFunc[] snapshot;
        lock (SyncLock) {
            snapshot = RegistryFuncs.ToArray();
        }

        foreach (var registryFunc in snapshot) {
            registryFunc(serviceCollection);
        }
    }

    /// <summary>
    /// Apply all decorators
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection) {
        DecoratorRegistration[] snapshot;
        lock (SyncLock) {
            snapshot = Decorators.ToArray();
        }

        // OrderBy is a stable sort, so decorators sharing an order keep their registration order
        // rather than nesting arbitrarily.
        foreach (var decorator in snapshot.OrderBy(decorator => decorator.Order)) {
            decorator.RegistryFunc(serviceCollection);
        }
    }

    private readonly struct DecoratorRegistration(int order, RegistryFunc registryFunc) {
        public int Order { get; } = order;

        public RegistryFunc RegistryFunc { get; } = registryFunc;
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
        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyDecorators(serviceCollection);
        }
    }

    private static void ApplyServices(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        IModuleEnvironment? environment = null;
        var hasEnvironmentModules = false;

        for (var i = 0; i < modules.Count; i++) {
            if (modules[i] is IEnvironmentServiceCollectionConfiguration) {
                hasEnvironmentModules = true;
                break;
            }
        }

        if (hasEnvironmentModules) {
            environment = FindModuleEnvironment(serviceCollection);
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyServices(serviceCollection);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }

            if (module is IEnvironmentServiceCollectionConfiguration environmentConfigure) {
                environmentConfigure.ConfigureServices(serviceCollection, environment);
            }
        }
    }

    private static IModuleEnvironment? FindModuleEnvironment(IServiceCollection serviceCollection) {
        for (var i = serviceCollection.Count - 1; i >= 0; i--) {
            var descriptor = serviceCollection[i];
            if (descriptor.ServiceType == typeof(IModuleEnvironment) &&
                descriptor.Lifetime == ServiceLifetime.Singleton &&
                descriptor.ImplementationInstance is IModuleEnvironment environment) {
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
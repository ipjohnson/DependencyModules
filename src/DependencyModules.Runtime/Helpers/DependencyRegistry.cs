using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

public delegate void RegistryFunc(IServiceCollection serviceCollection);

/// <summary>
///     Static class used to store dependency registration functions
///     per type
/// </summary>
/// <typeparam name="T"></typeparam>
// ReSharper disable once ClassNeverInstantiated.Global
public class DependencyRegistry<T> {
    // ReSharper disable StaticMemberInGenericType
    private static readonly List<RegistryFunc> RegistryFuncs = [];
    private static readonly List<RegistryFunc> Decorators = [];

    /// <summary>
    ///     Add registration func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(RegistryFunc registryFunc) {
        RegistryFuncs.Add(registryFunc);

        return 1;
    }

    /// <summary>
    /// Adding singleton instance, intended to be used as a short cut 
    /// </summary>
    /// <param name="instance"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static int AddSingleton<TInstance>(TInstance instance) where TInstance : class {
        RegistryFuncs.Add(registry => registry.AddSingleton<TInstance>(instance));
        return 1;
    }
    
    /// <summary>
    ///      Add decorator func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int AddDecorator(RegistryFunc registryFunc) {
        Decorators.Add(registryFunc);
        
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

    private static void ApplyDecorators(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyDecorators(serviceCollection);
        }
    }

    private static void ApplyServices(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyServices(serviceCollection);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }
        }
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

    /// <summary>
    ///     Apply all registration for a given type to the service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyServices(IServiceCollection serviceCollection) {
        foreach (var registryFunc in RegistryFuncs) {
            registryFunc(serviceCollection);
        }
    }
    
    /// <summary>
    /// Apply all decorators
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection) {
        foreach (var registryFunc in Decorators) {
            registryFunc(serviceCollection);
        }
    }
    
    private static void InternalGetModules(IDependencyModule dependencyModule, List<IDependencyModule> dependencyModules) {
        if (!dependencyModule.LoadModule || 
            dependencyModules.Contains(dependencyModule)) {
            return;
        }

        dependencyModules.Insert(0, dependencyModule);

        foreach (var dependentModule in dependencyModule.InternalGetModules()) {
            if (dependentModule is IDependencyModuleProvider moduleProvider) {
                var dep = moduleProvider.GetModule();
                InternalGetModules(dep, dependencyModules);
            }
            else if (dependencyModule is IDependencyModule module) {
                InternalGetModules(module, dependencyModules);
            }
        }
        
        foreach (var module in dependencyModule.GetModules()) {
            InternalGetModules(module, dependencyModules);
        } 
    }
}
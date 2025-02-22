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
    // ReSharper disable once StaticMemberInGenericType
    private static readonly List<RegistryFunc> _registryFuncs = [];
    private static readonly List<RegistryFunc> _decorators = [];

    /// <summary>
    ///     Add registration func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(RegistryFunc registryFunc) {
        _registryFuncs.Add(registryFunc);

        return 1;
    }

    /// <summary>
    ///      Add decorator func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int AddDecorator(RegistryFunc registryFunc) {
        _decorators.Add(registryFunc);
        
        return 1;
    }

    /// <summary>
    ///     Load modules into service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="dependencyModules"></param>
    public static void LoadModules(IServiceCollection serviceCollection, params IDependencyModule[] dependencyModules) {
        var modules = GetAllModules(dependencyModules);
        
        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyServices(serviceCollection);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];

            if (module is IDependencyModuleApplicatorProvider provider) {
                foreach (var featureApplicator in provider.FeatureApplicators()) {
                    featureApplicator.Apply(serviceCollection, modules);
                }
            }
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyDecorators(serviceCollection);
        }
    }

    /// <summary>
    ///     Get list of modules from module. Inclusive
    /// </summary>
    /// <param name="dependencyModules"></param>
    /// <returns></returns>
    public static IReadOnlyList<IDependencyModule> GetAllModules(IDependencyModule[] dependencyModules) {
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
        foreach (var registryFunc in _registryFuncs) {
            registryFunc(serviceCollection);
        }
    }
    
    /// <summary>
    /// Apply all decorators
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection) {
        foreach (var registryFunc in _decorators) {
            registryFunc(serviceCollection);
        }
    }
    
    private static void InternalGetModules(IDependencyModule dependencyModule, List<IDependencyModule> dependencyModules) {
        if (dependencyModules.Contains(dependencyModule)) {
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
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
    ///     Load modules into service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="dependencyModules"></param>
    public static void LoadModules(IServiceCollection serviceCollection, params IDependencyModule[] dependencyModules) {
        foreach (var module in GetAllModules(dependencyModules)) {
            module.InternalApplyServices(serviceCollection);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }
        }
    }

    /// <summary>
    ///     Get list of modules from module. Inclusive
    /// </summary>
    /// <param name="dependencyModules"></param>
    /// <returns></returns>
    public static IEnumerable<IDependencyModule> GetAllModules(IDependencyModule[] dependencyModules) {
        var list = new List<IDependencyModule>();

        foreach (var dependencyModule in dependencyModules) {
            InternalGetModules(dependencyModule, list);
        }

        return list;
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

    /// <summary>
    ///     Apply all registration for a given type to the service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyServices(IServiceCollection serviceCollection) {
        foreach (var registryFunc in _registryFuncs) {
            registryFunc(serviceCollection);
        }
    }
}
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime;

public static class ServiceCollectionExtensions {
    /// <summary>
    ///     Add dependency module to service collection
    /// </summary>
    /// <param name="services"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IServiceCollection AddModule<T>(this IServiceCollection services)
        where T : IDependencyModule, new() {
        return AddModule(services, new T());
    }

    /// <summary>
    ///     Add dependency module to service collection
    /// </summary>
    /// <param name="services"></param>
    /// <param name="module"></param>
    /// <returns></returns>
    // ReSharper disable once MemberCanBePrivate.Global
    public static IServiceCollection AddModule(this IServiceCollection services, IDependencyModule module) {
        module.PopulateServiceCollection(services);

        return services;
    }
    
    
    /// <summary>
    ///     Add dependency modules to service collection
    /// </summary>
    /// <param name="services"></param>
    /// <param name="modules"></param>
    /// <returns></returns>
    public static IServiceCollection AddModules(this IServiceCollection services, params IDependencyModule[] modules) {
        DependencyRegistry<object>.LoadModules(services, modules);
        
        return services;
    }
}
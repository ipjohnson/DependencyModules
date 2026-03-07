using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     DependencyModules that want to do programmatic registration should implement this interface.
/// </summary>
public interface IServiceCollectionConfiguration {
    /// <summary>
    /// Configure service in IServiceCollection
    /// </summary>
    /// <param name="services"></param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Called after all services have been registered allowing for decorating
    /// </summary>
    /// <param name="services"></param>
    void ConfigureDecorators(IServiceCollection services) { }
}

/// <summary>
///     DependencyModules that need access to the environment during registration should implement this interface.
/// </summary>
public interface IEnvironmentServiceCollectionConfiguration {
    /// <summary>
    /// Configure services with access to the module environment.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="environment"></param>
    void ConfigureServices(IServiceCollection services, IModuleEnvironment? environment);
}
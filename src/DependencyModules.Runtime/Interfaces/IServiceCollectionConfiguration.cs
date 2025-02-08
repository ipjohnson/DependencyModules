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
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     DependencyModules that want to do programmatic registration should implement this interface.
/// </summary>
public interface IServiceCollectionConfiguration {
    void ConfigureServices(IServiceCollection services);
}
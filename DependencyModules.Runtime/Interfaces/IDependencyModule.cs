using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
/// Internal interface not intended to be consumed by developers
/// </summary>
public interface IDependencyModule {
    void PopulateServiceCollection(IServiceCollection serviceCollection);
    
    IEnumerable<object> GetDependentModules();
    
    void ApplyServices(IServiceCollection serviceCollection);
}
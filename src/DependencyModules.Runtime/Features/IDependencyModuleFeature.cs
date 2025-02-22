using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

public interface IDependencyModuleFeature<in TFeature> {
    int Order => 0;

    void HandleFeature(IServiceCollection collection, IEnumerable<TFeature> feature);
}
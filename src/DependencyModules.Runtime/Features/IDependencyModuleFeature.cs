using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

public interface IDependencyModuleFeature<in TFeature> {
    void HandleFeature(IServiceCollection collection, IEnumerable<TFeature> feature);
}
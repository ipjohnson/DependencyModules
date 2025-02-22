using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

public class FeatureApplicator<TFeature>(IDependencyModuleFeature<TFeature> handler) : IFeatureApplicator {

    public void Apply(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        handler.HandleFeature(serviceCollection, modules.OfType<TFeature>());
    }
}
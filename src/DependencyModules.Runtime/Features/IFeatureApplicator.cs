using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

public interface IFeatureApplicator {
    void Apply(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules);
}
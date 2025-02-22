using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

public interface IFeatureApplicator {
    int Order { get; }
    
    void Apply(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules);
}
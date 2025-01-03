using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IDependencyOne {
    ISingletonService SingletonService { get; }
    IScopedService ScopedService { get; }
}

[TransientService(ServiceType = typeof(IDependencyOne))]
public class DependencyOne : IDependencyOne {
    public ISingletonService SingletonService { get; }
    public IScopedService ScopedService { get; }

    public DependencyOne(ISingletonService singletonService,
        IScopedService scopedService) {
        SingletonService = singletonService;
        ScopedService = scopedService;
    }
}
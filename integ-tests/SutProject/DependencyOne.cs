using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IDependencyOne {
    ISingletonService SingletonService { get; }
    IScopedService ScopedService { get; }
}

[TransientService]
public class DependencyOne(
    ISingletonService singletonService,
    IScopedService scopedService) : IDependencyOne {

    public ISingletonService SingletonService { get; } = singletonService;
    public IScopedService ScopedService { get; } = scopedService;
}
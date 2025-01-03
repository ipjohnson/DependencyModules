using DependencyModules.Runtime.Attributes;
using SutProject;

namespace SecondarySutProject;

[TransientService(ServiceType = typeof(IDependencyOne), UseTry = false)]
public class BetterDependencyOne : IDependencyOne {

    public BetterDependencyOne(ISingletonService singletonService, IScopedService scopedService) {
        SingletonService = singletonService;
        ScopedService = scopedService;
    }

    public ISingletonService SingletonService {
        get;
    }

    public IScopedService ScopedService {
        get;
    }
}
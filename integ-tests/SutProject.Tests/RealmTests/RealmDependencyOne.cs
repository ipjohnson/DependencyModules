using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.RealmTests;

[TransientService(As = typeof(IDependencyOne), Realm = typeof(FirstRealmModule), Using = RegistrationType.Add)]
public class RealmDependencyOne : IDependencyOne {
    public RealmDependencyOne(ISingletonService singletonService, IScopedService scopedService) {
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
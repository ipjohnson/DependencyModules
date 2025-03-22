using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.RegistrationTypeTests;

[DependencyModule(OnlyRealm = true)]
[SutModule]
public partial class ReplaceModule {
    
}

[SingletonService(Using = RegistrationType.Replace, Realm = typeof(ReplaceModule))]
public class ReplaceDependency : IDependencyOne {

    public ReplaceDependency(ISingletonService singletonService, IScopedService scopedService) {
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

public class ReplaceTests {
    [ModuleTest]
    [ReplaceModule]
    public void ReplaceTest(IEnumerable<IDependencyOne> dependencies) {
        var dependenciesList = dependencies.ToList();
        Assert.Single(dependenciesList);
        Assert.IsType<ReplaceDependency>(dependenciesList[0]);
    }
}
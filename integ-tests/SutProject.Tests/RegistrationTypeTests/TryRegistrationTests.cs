using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.RegistrationTypeTests;

[DependencyModule(OnlyRealm = true)]
[SutModule]
public partial class TryWithSutModule;

[DependencyModule(OnlyRealm = true)]
public partial class TryWithoutSutModule;

[DependencyModule(Using = RegistrationType.Try, OnlyRealm = true)]
[SutModule]
public partial class TryAtModuleLevelWithSutModule;

[SingletonService(Using = RegistrationType.Try, Realm = typeof(TryWithSutModule))]
[SingletonService(Using = RegistrationType.Try, Realm = typeof(TryWithoutSutModule))]
#pragma warning disable CS8618 
public class TryDependency : IDependencyOne {

    public ISingletonService SingletonService {
        get;
    }

    public IScopedService ScopedService {
        get;
    }
}


#pragma warning restore CS8618

public class TryRegistrationTests {
    [ModuleTest]
    [TryWithSutModule]
    public void  TryWithSut(IDependencyOne service) {
        Assert.IsType<DependencyOne>(service);
    }

    [ModuleTest]
    [TryWithoutSutModule]
    public void TryWithoutSut(IDependencyOne service) {
        Assert.IsType<TryDependency>(service);
    }

    [ModuleTest]
    [TryAtModuleLevelWithSutModule]
    public void TryModuleLevelSut(IDependencyOne service) {
        Assert.IsType<DependencyOne>(service);
    }
}
using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.RegistrationTypeTests;

[DependencyModule(OnlyRealm = true)]
[SutModule.Attribute]
public partial class TryWithSutModule {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class TryWithoutSutModule {
    
}

[SingletonService(With = RegistrationType.Try, Realm = typeof(TryWithSutModule))]
[SingletonService(With = RegistrationType.Try, Realm = typeof(TryWithoutSutModule))]
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
    [TryWithSutModule.Attribute]
    public void  TryWithSut(IDependencyOne service) {
        Assert.IsType<DependencyOne>(service);
    }

    [ModuleTest]
    [TryWithoutSutModule.Attribute]
    public void TryWithoutSut(IDependencyOne service) {
        Assert.IsType<TryDependency>(service);
    }
}
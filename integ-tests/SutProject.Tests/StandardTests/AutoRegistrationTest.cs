using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

[DependencyModule(OnlyRealm = true)]
[SutModule]
public partial class AutoRegisterModule {
    
}

[SingletonService(Realm = typeof(AutoRegisterModule))]
public class InheritDependencyOne
    (ISingletonService singletonService, IScopedService scopedService) 
    : DependencyOne(singletonService, scopedService), IDependencyOne {

}

public class AutoRegistrationTest {
    [ModuleTest]
    [AutoRegisterModule]
    public void AutoRegisterClassWithInheritance(IDependencyOne dependencyOne) {
        Assert.IsType<InheritDependencyOne>(dependencyOne);
    }
}
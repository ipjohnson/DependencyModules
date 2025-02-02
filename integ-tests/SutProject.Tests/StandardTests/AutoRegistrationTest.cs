using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

[DependencyModule(OnlyRealm = true)]
[SutModule.Attribute]
public partial class AutoRegisterModule {
    
}

[SingletonService(Realm = typeof(AutoRegisterModule))]
public class InheritDependencyOne
    (ISingletonService singletonService, IScopedService scopedService) 
    : DependencyOne(singletonService, scopedService), IDependencyOne {

}

public class AutoRegistrationTest {
    [ModuleTest]
    [AutoRegisterModule.Attribute]
    public void AutoRegisterClassWithInheritance(IDependencyOne dependencyOne) {
        Assert.IsType<InheritDependencyOne>(dependencyOne);
    }
}
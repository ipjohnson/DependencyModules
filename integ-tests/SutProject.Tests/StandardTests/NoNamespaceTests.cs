
using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using SutProject;
using Xunit;

[DependencyModule]
public partial class TestModule {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class NoNamespaceTestModule {
    
}

#pragma warning disable CS8618
[SingletonService(Realm = typeof(NoNamespaceTestModule))]
public class SomeDependency : IDependencyOne {
   public ISingletonService SingletonService {
        get;
    }

    public IScopedService ScopedService {
        get;
    }
}
#pragma warning restore CS8618 

public class NoNamespaceTests {
    [ModuleTest]
    [NoNamespaceTestModule.Attribute]
    public void NoNamespaceTest(IDependencyOne dependency) {
        Assert.NotNull(dependency);
        Assert.IsType<SomeDependency>(dependency);
    }
}
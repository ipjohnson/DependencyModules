
using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using SutProject;

[DependencyModule]
public partial class TestModule {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class NoNamespaceTestModule {
    
}

[SingletonService]
public class SomeDependency : IDependencyOne {

    public ISingletonService SingletonService {
        get;
    }

    public IScopedService ScopedService {
        get;
    }
}

public class NoNamespaceTests {
    [ModuleTest]
    [NoNamespaceTestModule.Attribute]
    public void NoNamespaceTest(IDependencyOne dependency) {
        
    }
}
using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.CrossWire;

[DependencyModule(OnlyRealm = true)]
public partial class CrossWireModule {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class CrossWireModuleScoped {
    
}

public interface IInterface1 {
    
}

public interface IInterface2 {
    
}

[CrossWireService(Realm = typeof(CrossWireModule))]
[CrossWireService(Lifetime = ServiceLifetime.Scoped)]
public class CrossWireService : IInterface1, IInterface2 {
    
}

public class CrossWireTests {
    [ModuleTest]
    [CrossWireModule.Attribute]
    public void SingletonCrossWireTest(IInterface1 interface1, IInterface2 interface2) {
        Assert.Same(interface1, interface1);
    }
    
    [ModuleTest]
    [CrossWireModule.Attribute]
    public void ScopedCrossWireTest(IInterface1 interface1, IInterface2 interface2) {
        Assert.Same(interface1, interface1);
    }
}
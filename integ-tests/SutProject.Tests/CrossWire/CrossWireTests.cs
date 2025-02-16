using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.CrossWire;

[DependencyModule(OnlyRealm = true)]
public partial class CrossWireModule {
    
}

public interface IInterface1 {
    
}

public interface IInterface2 {
    
}

[CrossWireService(Realm = typeof(CrossWireModule))]
public class CrossWireService : IInterface1, IInterface2 {
    
}

public class CrossWireTests {
    [ModuleTest]
    [CrossWireModule.Attribute]
    public void SimpleCrossWireTest(IInterface1 interface1, IInterface2 interface2) {
        Assert.Same(interface1, interface1);
    }
}
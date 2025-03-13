using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Xunit;

namespace SutProject.Tests.StandardTests;

public interface ICustomAttributeInterface {
    
}

public partial class AttributeTestModuleAttribute : ICustomAttributeInterface{
        
}

[DependencyModule(OnlyRealm = true)]
public partial class AttributeTestModule {

}

[DependencyModule]
[AttributeTestModule]
public partial class SomeModule {
    
}

public class AttributeTest {
    [Fact]
    public void AttributePartialTest() {
        var attributeType = typeof(AttributeTestModuleAttribute);

        var interfaces = attributeType.GetInterfaces();
        
        Assert.Contains(interfaces, i => i == typeof(ICustomAttributeInterface));
        Assert.Contains(interfaces, i => i == typeof(IDependencyModuleProvider));
    }
}
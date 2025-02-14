using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Xunit;

namespace SutProject.Tests.StandardTests;

public interface ICustomAttributeInterface {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class AttributeTestModule {
    public partial class Attribute : ICustomAttributeInterface{
        
    }
}

[DependencyModule]
[AttributeTestModule.Attribute]
public partial class SomeModule {
    
}

public class AttributeTest {
    [Fact]
    public void AttributePartialTest() {
        var attributeType = typeof(AttributeTestModule.Attribute);

        var interfaces = attributeType.GetInterfaces();
        
        Assert.Contains(interfaces, i => i == typeof(ICustomAttributeInterface));
        Assert.Contains(interfaces, i => i == typeof(IDependencyModuleProvider));
    }
}
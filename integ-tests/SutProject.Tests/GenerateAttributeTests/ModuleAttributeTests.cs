using System.Reflection;
using Xunit;

namespace SutProject.Tests.GenerateAttributeTests;

public class ModuleAttributeTests {
    [Fact]
    public void AssertGenerateAttribute() {
        var assembly = GetType().Assembly;

        var withAttributeType = assembly.GetType(typeof(ModuleWithAttribute).FullName + "+Attribute");
        
        Assert.NotNull(withAttributeType);
        
        var withoutAttributeType = assembly.GetType(typeof(ModuleWithoutAttribute).FullName + "+Attribute");
        
        Assert.Null(withoutAttributeType);
    }
}
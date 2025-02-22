using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.Features;

public class FeatureTests {
    [ModuleTest]
    [FeatureModuleHandler.Attribute]
    [FeatureModuleA.Attribute]
    [FeatureModuleB.Attribute]
    [FeatureModuleC.Attribute]
    public void FeatureTest(IEnumerable<DependencyValue> values) {
        var valuesArray = values.ToArray();
        
        Assert.Equal(3, valuesArray.Length);
        Assert.Single(valuesArray, v => v.Value == "A");
        Assert.Single(valuesArray, v => v.Value == "B");
        Assert.Single(valuesArray, v => v.Value == "C");
    }
}
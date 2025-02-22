using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.Features;

public class OrderFeatureTests {
    [ModuleTest]
    [FirstFeatureHandler.Attribute]
    [SecondFeatureHandler.Attribute]
    [ThirdFeatureHandler.Attribute]
    public void OrderTest(IEnumerable<DependencyValue> values) {
        var valuesList = values as List<DependencyValue> ?? values.ToList();
        
        Assert.Equal(3, valuesList.Count);
        Assert.Equal("1", valuesList[0].Value);
        Assert.Equal("2", valuesList[1].Value);
        Assert.Equal("3", valuesList[2].Value);
    }
}
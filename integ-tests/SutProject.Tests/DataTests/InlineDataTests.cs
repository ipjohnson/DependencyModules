using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DataTests;

public class InlineDataTests {

    [ModuleTest]
    [InlineData("Hello World")]
    [SutModule.Attribute]
    public void SimpleValueTests(string value, IDependencyOne one) {
        Assert.Equal("Hello World", value);
        Assert.NotNull(one);
    }
}
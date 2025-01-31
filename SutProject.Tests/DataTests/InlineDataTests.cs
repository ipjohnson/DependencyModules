using DependencyModules.Testing.Attributes;
using Xunit;

namespace SutProject.Tests.DataTests;

public class InlineDataTests {

    [ModuleTest]
    [InlineData("Hello World")]
    [SutModule.Module]
    public void SimpleValueTests(string value, IDependencyOne one) {
        Assert.Equal("Hello World", value);
        Assert.NotNull(one);
    }
}
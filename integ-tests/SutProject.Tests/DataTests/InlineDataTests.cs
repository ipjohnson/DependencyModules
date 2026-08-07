using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DataTests;

public class InlineDataTests {

    [ModuleTest]
    [InlineData("Hello World")]
    [SutModule]
    public void SimpleValueTests(string value, IDependencyOne one) {
        Assert.Equal("Hello World", value);
        Assert.NotNull(one);
    }
}

public class MultiRowDataTests {

    [ModuleTest]
    [InlineData("one")]
    [InlineData("two")]
    [InlineData("three")]
    [SutModule]
    public void MultipleRows(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }
}

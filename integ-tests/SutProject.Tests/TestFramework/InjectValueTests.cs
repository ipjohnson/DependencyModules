using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.TestFramework;

public record InjectModel(IDependencyOne DependencyOne, string StringValue);

public class InjectValueTests {
    [ModuleTest]
    [SutModule]
    public void InjectTestValue(
        [InjectValues("Hello World!")]InjectModel injectModel) {
        Assert.NotNull(injectModel);
        Assert.NotNull(injectModel.DependencyOne);
        Assert.Equal("Hello World!", injectModel.StringValue);
    }
}
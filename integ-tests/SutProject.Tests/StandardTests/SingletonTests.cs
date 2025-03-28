using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SingletonTests {
    [ModuleTest]
    [SutModule]
    public void ResolveSingleton(ISingletonService singletonService, ISingletonService otherSingletonService) {
        Assert.NotNull(singletonService);
        Assert.NotNull(otherSingletonService);
        Assert.Same(singletonService, otherSingletonService);
    }
}
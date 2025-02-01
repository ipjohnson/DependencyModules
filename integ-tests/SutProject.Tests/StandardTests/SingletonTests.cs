using DependencyModules.Testing.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SingletonTests {
    [ModuleTest]
    [SutModule.Module]
    public void ResolveSingleton(ISingletonService singletonService, ISingletonService otherSingletonService) {
        Assert.NotNull(singletonService);
        Assert.NotNull(otherSingletonService);
        Assert.Same(singletonService, otherSingletonService);
    }
}
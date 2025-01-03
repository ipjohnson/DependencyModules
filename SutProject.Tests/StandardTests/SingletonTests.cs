using DependencyModules.Testing.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SingletonTests {
    [ModuleTest]
    [LoadModules(typeof(SutModule))]
    public void ResolveSingleton(ISingletonService singletonService) {
        Assert.NotNull(singletonService);
    }
}
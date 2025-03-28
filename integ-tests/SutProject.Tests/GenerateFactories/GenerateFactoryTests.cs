using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.GenerateFactories;

public class GenerateFactoryTests {
    [ModuleTest]
    [GenerateFactoryModule]
    public void ConstructGeneratedFactories(
        KeyedDependency keyedDependency, IDependencyOne dep) {
        Assert.NotNull(keyedDependency);
        Assert.NotNull(dep);

        Assert.Null(dep.ScopedService);
        Assert.Null(dep.SingletonService);

        Assert.Equal("Keyed", keyedDependency.Registration.Key);
    }
}
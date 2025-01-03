using DependencyModules.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SimpleResolveTests {
    [ModuleTest]
    [LoadModules(typeof(SutModule))]
    public void Test(IDependencyOne dependencyOne) {
        Assert.NotNull(dependencyOne);
        Assert.NotNull(dependencyOne.SingletonService);
        Assert.NotNull(dependencyOne.ScopedService);
    }
}
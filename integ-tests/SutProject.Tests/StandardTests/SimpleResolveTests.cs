using DependencyModules.Testing.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SimpleResolveTests {
    [ModuleTest]
    [SutModule.Module]
    public void SimpleTest(IDependencyOne dependencyOne) {
        Assert.NotNull(dependencyOne);
        Assert.NotNull(dependencyOne.SingletonService);
        Assert.NotNull(dependencyOne.ScopedService);
    }


    [ModuleTest]
    [SutModule.Module]
    public void ResolveServiceProvider(IDependencyOne dependencyOne, IServiceProvider serviceProvider) {
        Assert.NotNull(dependencyOne);
        Assert.NotNull(serviceProvider);
    }
}
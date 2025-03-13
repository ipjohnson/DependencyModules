using DependencyModules.xUnit.Attributes;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SecondarySutProjectTests {
    [ModuleTest]
    [SecondarySutModule]
    public void OverrideDependency(IDependencyOne dependencyOne) {
        Assert.IsType<BetterDependencyOne>(dependencyOne);
    }
}
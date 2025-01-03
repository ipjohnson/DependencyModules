using DependencyModules.Testing.Attributes;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SecondarySutProjectTests {
    [ModuleTest]
    [LoadModules(typeof(SecondarySutModule))]
    public void OverrideDependency(IDependencyOne dependencyOne) {
        Assert.IsType<BetterDependencyOne>(dependencyOne);
    }
}
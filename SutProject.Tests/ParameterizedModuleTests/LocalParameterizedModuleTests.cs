using DependencyModules.Runtime.Attributes;
using DependencyModules.Testing.Attributes;
using SecondarySutProject.ParameterizedModules;
using Xunit;

namespace SutProject.Tests.ParameterizedModuleTests;

public class LocalParameterizedModuleTests {
    [ModuleTest(typeof(LocalParameterizedModule))]
    public void ParameterTest(SomeRuntimeDependency someRuntimeDependency) {
        Assert.Equal("local-string", someRuntimeDependency.SomeDependency);
        Assert.Equal(20, someRuntimeDependency.IntDependency);
    }
}
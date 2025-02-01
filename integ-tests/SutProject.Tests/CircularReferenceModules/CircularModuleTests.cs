using DependencyModules.Testing.Attributes;
using SecondarySutProject.CircularReferenceModules;
using Xunit;

namespace SutProject.Tests.CircularReferenceModules;

public class CircularModuleTests {

    [ModuleTest]
    [ModuleA.Module]
    public void LoadModuleATest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }


    [ModuleTest]
    [ModuleB.Module]
    public void LoadModuleBTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }


    [ModuleTest]
    [ModuleA.Module]
    [ModuleB.Module]
    public void LoadModuleBothTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }
}
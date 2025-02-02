using DependencyModules.xUnit.Attributes;
using SecondarySutProject.CircularReferenceModules;
using Xunit;

namespace SutProject.Tests.CircularReferenceModules;

public class CircularModuleTests {

    [ModuleTest]
    [ModuleA.Attribute]
    public void LoadModuleATest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }


    [ModuleTest]
    [ModuleB.Attribute]
    public void LoadModuleBTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }


    [ModuleTest]
    [ModuleA.Attribute]
    [ModuleB.Attribute]
    public void LoadModuleBothTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }
}
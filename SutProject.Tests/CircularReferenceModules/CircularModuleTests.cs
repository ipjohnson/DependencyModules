using DependencyModules.Testing.Attributes;
using SecondarySutProject.CircularReferenceModules;
using Xunit;

namespace SutProject.Tests.CircularReferenceModules;

public class CircularModuleTests {

    [ModuleTest]
    [LoadModules(typeof(ModuleA))]
    public void LoadModuleATest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }

    
    [ModuleTest]
    [LoadModules(typeof(ModuleB))]
    public void LoadModuleBTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }
    
        
    [ModuleTest]
    [LoadModules(typeof(ModuleA),typeof(ModuleB))]
    public void LoadModuleBothTest(ServiceA serviceA, ServiceB serviceB) {
        Assert.NotNull(serviceA);
        Assert.NotNull(serviceB);
    }
}
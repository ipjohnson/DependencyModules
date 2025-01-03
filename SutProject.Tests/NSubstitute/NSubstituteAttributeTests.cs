using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.NSubstitute;
using NSubstitute;
using Xunit;

namespace SutProject.Tests.NSubstitute;

[NSubstituteSupport]
public class NSubstituteAttributeTests {
    
    [ModuleTest]
    [LoadModules(typeof(SutModule))]
    public void MockTest([Mock]IDependencyOne dependencyOne,
        [Mock]IScopedService scopedService, ISingletonService singletonService) {
        dependencyOne.SingletonService.Returns(singletonService);
        dependencyOne.ScopedService.Returns(scopedService);
        
        Assert.Same(dependencyOne.SingletonService, singletonService);
        Assert.Same(dependencyOne.ScopedService, scopedService);
    }
}
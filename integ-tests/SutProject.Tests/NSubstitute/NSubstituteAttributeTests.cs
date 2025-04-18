using DependencyModules.xUnit.Attributes;
using DependencyModules.xUnit.NSubstitute;
using NSubstitute;
using Xunit;

namespace SutProject.Tests.NSubstitute;

[NSubstituteSupport]
public class NSubstituteAttributeTests {

    [ModuleTest]
    [SutModule]
    public void MockTest([Mock] IDependencyOne dependencyOne,
        [Mock] IScopedService scopedService, ISingletonService singletonService) {
        dependencyOne.SingletonService.Returns(singletonService);
        dependencyOne.ScopedService.Returns(scopedService);

        Assert.Same(dependencyOne.SingletonService, singletonService);
        Assert.Same(dependencyOne.ScopedService, scopedService);
    }
}
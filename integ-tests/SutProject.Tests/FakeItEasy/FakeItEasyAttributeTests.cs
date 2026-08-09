using DependencyModules.FakeItEasy;
using DependencyModules.xUnit.Attributes;
using FakeItEasy;
using Xunit;

namespace SutProject.Tests.FakeItEasy;

/// <summary>
/// The same scenario as the NSubstitute and Moq tests, so the three can be read against each other.
/// </summary>
[FakeItEasySupport]
public class FakeItEasyAttributeTests {

    [ModuleTest]
    [SutModule]
    public void MockTest([Mock] IDependencyOne dependencyOne,
        [Mock] IScopedService scopedService, ISingletonService singletonService) {
        A.CallTo(() => dependencyOne.SingletonService).Returns(singletonService);
        A.CallTo(() => dependencyOne.ScopedService).Returns(scopedService);

        Assert.Same(dependencyOne.SingletonService, singletonService);
        Assert.Same(dependencyOne.ScopedService, scopedService);
    }

    /// <summary>
    /// The injected fake is the thing you configure, unlike Moq — no unwrapping step.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void TheInjectedInstanceIsTheFake([Mock] IDependencyOne dependencyOne) {
        Assert.True(Fake.GetFakeManager(dependencyOne) is not null);
    }
}

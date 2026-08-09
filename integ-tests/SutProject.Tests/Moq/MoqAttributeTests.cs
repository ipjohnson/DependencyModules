using DependencyModules.Moq;
using DependencyModules.xUnit.Attributes;
using Moq;
using Xunit;

namespace SutProject.Tests.Moq;

/// <summary>
/// The same scenario as the NSubstitute and FakeItEasy tests, so the three can be read against each
/// other. Moq is the one that separates the mock from the object, and this is what that costs at the
/// call site: <c>Mock.Get</c> to reach the setup.
/// </summary>
[MoqSupport]
public class MoqAttributeTests {

    [ModuleTest]
    [SutModule]
    public void MockTest([Mock] IDependencyOne dependencyOne,
        [Mock] IScopedService scopedService, ISingletonService singletonService) {
        Mock.Get(dependencyOne).Setup(x => x.SingletonService).Returns(singletonService);
        Mock.Get(dependencyOne).Setup(x => x.ScopedService).Returns(scopedService);

        Assert.Same(dependencyOne.SingletonService, singletonService);
        Assert.Same(dependencyOne.ScopedService, scopedService);
    }

    /// <summary>
    /// An unconfigured member returns default rather than throwing, matching Moq's own loose default.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void UnconfiguredMembersAreLoose([Mock] IDependencyOne dependencyOne) {
        Assert.Null(dependencyOne.ScopedService);
    }
}

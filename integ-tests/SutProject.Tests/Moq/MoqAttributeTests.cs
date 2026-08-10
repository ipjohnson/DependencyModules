using DependencyModules.Moq;
using DependencyModules.xUnit.Attributes;
using Moq;
using Xunit;

namespace SutProject.Tests.Moq;

/// <summary>
/// The same scenario as the NSubstitute and FakeItEasy tests, so the three can be read against each
/// other, plus the cases that only arise for Moq. Moq is the one that separates the mock from the
/// object, so a test can name either — and the two have to agree about which mock they mean.
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

    /// <summary>
    /// A <c>Mock&lt;T&gt;</c> parameter needs no attribute — the type already says what it is — and
    /// naming it replaces the service for the whole test, not just for the parameter holding it.
    /// </summary>
    /// <remarks>
    /// The second assertion is the one that matters. Asking only whether the mock can be configured
    /// would pass even with the registration removed, because an unregistered <c>Mock&lt;T&gt;</c>
    /// parameter is constructed by the container as an ordinary concrete type and behaves like a mock
    /// nothing else can see.
    /// </remarks>
    [ModuleTest]
    [SutModule]
    public void MockOfTIsInjectedDirectly(
        Mock<ISingletonService> mock, ISingletonService singletonService) {
        mock.Setup(x => x.GetName()).Returns("mocked");

        Assert.Same(mock.Object, singletonService);
        Assert.Equal("mocked", singletonService.GetName());
    }

    /// <summary>
    /// [Mock] on a <c>Mock&lt;T&gt;</c> is redundant rather than wrong, so a test written either way
    /// behaves the same. Without the unwrap in ProvideMock this asks Moq to mock a Mock.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void MockOfTIsInjectedWithTheAttributeToo(
        [Mock] Mock<ISingletonService> mock, ISingletonService singletonService) {
        mock.Setup(x => x.GetName()).Returns("mocked");

        Assert.Same(mock.Object, singletonService);
        Assert.Equal("mocked", singletonService.GetName());
    }

    /// <summary>
    /// The point of the whole thing: naming the mock replaces the service in the container, so the
    /// real DependencyOne is constructed against it. Registering only the Mock&lt;T&gt; would leave
    /// this holding a mock nothing else can see.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void ServiceUnderTestIsBuiltAgainstTheMock(
        IDependencyOne dependencyOne, Mock<ISingletonService> singletonService) {
        singletonService.Setup(x => x.GetName()).Returns("mocked");

        Assert.Same(singletonService.Object, dependencyOne.SingletonService);
        Assert.Equal("mocked", dependencyOne.SingletonService.GetName());
    }

    /// <summary>
    /// Asking for both spellings of one service is asking for two views of a single mock. Two
    /// mechanisms register this type — [Mock] and the Mock&lt;T&gt; scan — and if they disagreed the
    /// test would configure one mock while the container handed out another.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void TheMockAndTheInstanceAreOnePair(
        [Mock] ISingletonService instance, Mock<ISingletonService> mock) {
        mock.Setup(x => x.GetName()).Returns("mocked");

        Assert.Same(mock.Object, instance);
        Assert.Equal("mocked", instance.GetName());
    }

    /// <summary>
    /// Two parameters naming one service are one mock, so a setup made through either is visible
    /// through the other.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void RepeatedMockParametersShareOneMock(
        Mock<ISingletonService> first, Mock<ISingletonService> second) {
        first.Setup(x => x.GetName()).Returns("mocked");

        Assert.Same(first, second);
        Assert.Equal("mocked", second.Object.GetName());
    }

    /// <summary>
    /// Distinct services stay distinct — the scan keys on the mocked type, not on being a
    /// Mock&lt;T&gt; — and both land in the graph, so one mocked dependency does not crowd out
    /// another.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void DifferentServicesGetDifferentMocks(
        Mock<ISingletonService> singletonService,
        Mock<IScopedService> scopedService,
        IDependencyOne dependencyOne) {
        Assert.Same(singletonService.Object, dependencyOne.SingletonService);
        Assert.Same(scopedService.Object, dependencyOne.ScopedService);
    }

    /// <summary>
    /// Naming a real implementation beats mocking it. Mock support registers first within the setup
    /// pass precisely so this holds, and holds wherever [MoqSupport] is applied — here it is on the
    /// class and [TestExport] is on the method, but the outcome does not depend on that.
    /// </summary>
    [ModuleTest]
    [SutModule]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
    public void TestExportStillWinsOverAMock(
        ISingletonService instance, Mock<ISingletonService> mock) {
        Assert.IsType<ExportedSingletonService>(instance);
        Assert.NotSame(mock.Object, instance);
    }

    public class ExportedSingletonService : ISingletonService {
        public string GetName() => "exported";
    }
}

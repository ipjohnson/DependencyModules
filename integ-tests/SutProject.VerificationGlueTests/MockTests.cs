using DependencyModules.VerificationGlue.Attributes;
using VerificationGlue.Mocks;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;

namespace SutProject.VerificationGlueTests;

/// <summary>
/// A <c>[Mock]</c> parameter replaces the service in the module container.
/// </summary>
[ModuleTest]
[SutModule]
public partial class MockTests
{
    [VerifyTest]
    public void TheServiceReceivesTheMock(
        [Mock] ISingletonService singletonService,
        IDependencyOne dependencyOne
    )
    {
        singletonService.GetName().Returns("mocked");

        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
        Verify(dependencyOne.SingletonService.GetName() == "mocked");
    }

    [VerifyTest]
    public void TheContainerReturnsTheMock(
        [Mock] ISingletonService singletonService,
        IServiceProvider serviceProvider
    )
    {
        Verify(
            ReferenceEquals(serviceProvider.GetService(typeof(ISingletonService)), singletonService)
        );
    }

    /// <summary>
    /// The mock of the test before does not reach this test's container.
    /// </summary>
    [VerifyTest]
    public void AnotherTestGetsTheRealService(IDependencyOne dependencyOne)
    {
        Verify(dependencyOne.SingletonService is SingletonService);
    }
}

/// <summary>
/// The test class constructor and the test method share one mock.
/// </summary>
[ModuleTest<SutModule>]
public sealed partial class ConstructorMockTests([Mock] ISingletonService singletonService)
{
    [VerifyTest]
    public void TheConstructorAndTheMethodShareTheMock(
        [Mock] ISingletonService sameService,
        IDependencyOne dependencyOne
    )
    {
        Verify(ReferenceEquals(singletonService, sameService));
        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
    }
}

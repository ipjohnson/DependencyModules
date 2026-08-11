using DependencyModules.NSubstitute;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using NSubstitute;
using NUnit.Framework;

namespace SutProject.NUnitTests.NSubstitute;

/// <summary>
/// The NSubstitute package, unchanged, against NUnit.
/// </summary>
/// <remarks>
/// It references no test framework — it implements the hooks in <c>DependencyModules.Testing</c> —
/// so this is the payoff rather than new work: the same <c>[Mock]</c> attribute and the same
/// support attribute an xUnit test uses. Deliberately the same scenario as
/// <c>SutProject.Tests.NSubstitute.NSubstituteAttributeTests</c>, so the two can be read against
/// each other.
/// </remarks>
[NSubstituteSupport]
public class NSubstituteTests {

    [ModuleTest]
    [SutModule]
    public void MockTest(
        [Mock] IDependencyOne dependencyOne,
        [Mock] IScopedService scopedService,
        ISingletonService singletonService) {
        dependencyOne.SingletonService.Returns(singletonService);
        dependencyOne.ScopedService.Returns(scopedService);

        Assert.That(dependencyOne.SingletonService, Is.SameAs(singletonService));
        Assert.That(dependencyOne.ScopedService, Is.SameAs(scopedService));
    }

    /// <summary>A mocked service is the one the container hands to everything else, too.</summary>
    [ModuleTest]
    [SutModule]
    public void AMockReplacesTheRegistrationForTheWholeContainer(
        [Mock] IScopedService scopedService, IDependencyOne dependencyOne) {
        Assert.That(dependencyOne.ScopedService, Is.SameAs(scopedService));
    }

    /// <summary>
    /// Each iteration builds its own container, so a mock configured in one cannot be seen by the
    /// next. Written as a repeated test because that is the case a per-case container would break.
    /// </summary>
    [ModuleTest]
    [SutModule]
    [Repeat(3)]
    public void EachIterationGetsAFreshMock([Mock] IScopedService scopedService) {
        Assert.That(Seen.Add(scopedService), Is.True, "a mock instance is never reused across iterations");
    }

    private static readonly HashSet<IScopedService> Seen = [];
}

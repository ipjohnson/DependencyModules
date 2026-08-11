using DependencyModules.FakeItEasy;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using FakeItEasy;
using NUnit.Framework;

namespace SutProject.NUnitTests.FakeItEasy;

/// <summary>
/// The FakeItEasy package, unchanged, against NUnit.
/// </summary>
[FakeItEasySupport]
public class FakeItEasyTests {

    [ModuleTest]
    [SutModule]
    public void MockTest(
        [Mock] IDependencyOne dependencyOne,
        [Mock] IScopedService scopedService,
        ISingletonService singletonService) {
        A.CallTo(() => dependencyOne.SingletonService).Returns(singletonService);
        A.CallTo(() => dependencyOne.ScopedService).Returns(scopedService);

        Assert.That(dependencyOne.SingletonService, Is.SameAs(singletonService));
        Assert.That(dependencyOne.ScopedService, Is.SameAs(scopedService));
    }

    /// <summary>The injected fake is the thing you configure, unlike Moq — no unwrapping step.</summary>
    [ModuleTest]
    [SutModule]
    public void TheInjectedInstanceIsTheFake([Mock] IDependencyOne dependencyOne) {
        Assert.That(Fake.GetFakeManager(dependencyOne), Is.Not.Null);
    }
}

using DependencyModules.Moq;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using Moq;
using NUnit.Framework;

namespace SutProject.NUnitTests.Moq;

/// <summary>
/// The Moq package, unchanged, against NUnit.
/// </summary>
[MoqSupport]
public class MoqTests {

    [ModuleTest]
    [SutModule]
    public void MockTest(
        [Mock] Mock<IDependencyOne> dependencyOne, ISingletonService singletonService) {
        dependencyOne.Setup(mock => mock.SingletonService).Returns(singletonService);

        Assert.That(dependencyOne.Object.SingletonService, Is.SameAs(singletonService));
    }

    /// <summary>
    /// <c>[TestExport]</c> names a real implementation, and has to beat the mock whichever order the
    /// two are declared in. This is the arrangement declaration order alone would get wrong: the
    /// mock support is on the class, so it reaches the setup pass first and would otherwise be the
    /// later registration to win.
    /// </summary>
    [ModuleTest]
    [SutModule]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
    public void TestExportBeatsAMockOfTheSameService(ISingletonService singletonService) {
        Assert.That(singletonService, Is.TypeOf<ExportedSingletonService>());
    }
}

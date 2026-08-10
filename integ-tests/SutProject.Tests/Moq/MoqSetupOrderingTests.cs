using DependencyModules.Moq;
using DependencyModules.xUnit.Attributes;
using Moq;
using Xunit;

namespace SutProject.Tests.Moq;

/// <summary>
/// A real implementation, so a test can tell it apart from a mock of the same service.
/// </summary>
/// <remarks>
/// Declared outside the fixture because the attribute naming it sits on the fixture itself, and
/// attribute arguments there resolve in the enclosing scope rather than the class's own.
/// </remarks>
public class ExportedSingletonService : ISingletonService {
    public string GetName() => "exported";
}

/// <summary>
/// Pins which of [TestExport] and [MoqSupport] wins when they disagree.
/// </summary>
/// <remarks>
/// Both register through <c>ITestServiceSetupAttribute</c>, so they run in one pass over the
/// attributes in scope and the later registration wins. Attributes reach that pass widest scope
/// first — assembly, then class, then method — so declaration order alone would hand the outcome to
/// whichever of the two happens to sit nearer the method.
///
/// This is the arrangement where that disagrees with what should happen: <c>[TestExport]</c> on the
/// class, <c>[MoqSupport]</c> on the method. Left to declaration order the mock would register last
/// and win, quietly discarding an explicit registration. <c>ModuleTestCase</c> sorts mock support to
/// the front of the pass so that it cannot — a mock is the stand-in a test falls back to, and naming
/// a real implementation has to beat it.
///
/// <see cref="MoqAttributeTests.TestExportStillWinsOverAMock"/> covers the opposite arrangement,
/// which agrees with declaration order and so does not exercise the sort at all. Remove the sort and
/// that test still passes; this one does not.
/// </remarks>
[TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
public class MoqSetupOrderingTests {

    [ModuleTest]
    [SutModule]
    [MoqSupport]
    public void ExplicitRegistrationBeatsAMockDeclaredNearerTheMethod(
        ISingletonService instance, Mock<ISingletonService> mock) {
        Assert.IsType<ExportedSingletonService>(instance);
        Assert.Equal("exported", instance.GetName());

        // The mock was still made and registered — this is the sort deciding which registration the
        // service resolves to, not mock support failing to run.
        Assert.NotSame(mock.Object, instance);
    }
}

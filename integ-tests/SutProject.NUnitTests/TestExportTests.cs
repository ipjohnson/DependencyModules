using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace SutProject.NUnitTests;

/// <summary>
/// Stands in for the real singleton, so a test can tell which of two registrations survived.
/// </summary>
public class ExportedSingletonService : ISingletonService {
    public string GetName() => nameof(ExportedSingletonService);
}

/// <summary>
/// <c>[TestExport]</c> with no mocking package involved.
/// </summary>
/// <remarks>
/// The attribute now lives in <c>DependencyModules.Testing</c> rather than in an integration, which
/// is why it is available here at all — it registers through <c>ITestServiceSetupAttribute</c> and
/// never needed a test framework.
/// </remarks>
public class TestExportTests {

    [ModuleTest]
    [SutModule]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
    public void OverridesARegistrationForOneTest(ISingletonService singletonService) {
        Assert.That(singletonService, Is.TypeOf<ExportedSingletonService>());
    }

    /// <summary>
    /// The override is scoped to the test that asked for it. Nothing tears it down explicitly —
    /// the container it was registered in no longer exists.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void TheOverrideDoesNotLeakIntoTheNextTest(ISingletonService singletonService) {
        Assert.That(singletonService, Is.TypeOf<SingletonService>());
    }

    [ModuleTest]
    [SutModule]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService),
        Lifetime = ServiceLifetime.Singleton)]
    public void HonoursTheLifetimeItIsGiven(ISingletonService first, IServiceProvider serviceProvider) {
        Assert.That(serviceProvider.GetRequiredService<ISingletonService>(), Is.SameAs(first));
    }
}

using DependencyModules.Testing.Attributes;
using DependencyModules.VerificationGlue.Attributes;
using Microsoft.Extensions.DependencyInjection;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;
using MockAttribute = VerificationGlue.Mocks.MockAttribute;

namespace SutProject.VerificationGlueTests;

/// <summary>
/// Stands in for the real singleton, so a test can tell which registration won.
/// </summary>
public class ExportedSingletonService : ISingletonService
{
    public string GetName() => nameof(ExportedSingletonService);
}

/// <summary>
/// <c>[TestExport]</c> from DependencyModules.Testing, on a method and on a class.
/// </summary>
/// <remarks>
/// The file imports both DependencyModules.Testing.Attributes and the VerificationGlue
/// <c>[Mock]</c>, so it names the one it means.
/// </remarks>
[ModuleTest]
[SutModule]
public partial class TestExportTests
{
    [VerifyTest]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
    public void OverridesARegistrationForOneTest(ISingletonService singletonService)
    {
        Verify(singletonService is ExportedSingletonService);
    }

    [VerifyTest]
    public void TheOverrideDoesNotLeakIntoTheNextTest(ISingletonService singletonService)
    {
        Verify(singletonService is SingletonService);
    }

    [VerifyTest]
    [TestExport(
        typeof(ISingletonService),
        Implementation = typeof(ExportedSingletonService),
        Lifetime = ServiceLifetime.Singleton
    )]
    public void HonoursTheLifetimeItIsGiven(
        ISingletonService first,
        IServiceProvider serviceProvider
    )
    {
        Verify(ReferenceEquals(serviceProvider.GetRequiredService<ISingletonService>(), first));
    }

    /// <summary>
    /// The narrowest thing a test says about one service is a <c>[Mock]</c> parameter, so it
    /// replaces the export.
    /// </summary>
    [VerifyTest]
    [TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
    public void AMockReplacesAnExport(
        [Mock] ISingletonService singletonService,
        IDependencyOne dependencyOne
    )
    {
        Verify(singletonService is not ExportedSingletonService);
        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
    }
}

[ModuleTest]
[SutModule]
[TestExport(typeof(ISingletonService), Implementation = typeof(ExportedSingletonService))]
public partial class ClassTestExportTests
{
    [VerifyTest]
    public void TheClassExportReachesEachTest(IDependencyOne dependencyOne)
    {
        Verify(dependencyOne.SingletonService is ExportedSingletonService);
    }
}

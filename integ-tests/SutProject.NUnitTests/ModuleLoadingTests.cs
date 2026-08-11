using DependencyModules.NUnit.Attributes;
using DependencyModules.NUnit.Impl;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace SutProject.NUnitTests;

[DependencyModule(OnlyRealm = true)]
public partial class ExtraModule { }

[SingletonService(Realm = typeof(ExtraModule))]
public class ExtraService { }

/// <summary>
/// The two ways a test names its modules, and what a resolved parameter can be.
/// </summary>
/// <remarks>
/// No <c>[TestFixture]</c>, deliberately. <c>[ModuleTest]</c> implies a fixture the way
/// <c>[Test]</c> does, so a module test fixture needs no class-level attribute — the same as the
/// xUnit integration.
/// </remarks>
public class ModuleLoadingTests {

    /// <summary>Modules named on the attribute itself.</summary>
    [ModuleTest(typeof(SutModule))]
    public void LoadsAModuleNamedByType(ISingletonService singletonService) {
        Assert.That(singletonService, Is.Not.Null);
        Assert.That(singletonService.GetName(), Is.EqualTo(nameof(SingletonService)));
    }

    /// <summary>The generated module attribute, which reaches the same loading by another route.</summary>
    [ModuleTest]
    [SutModule]
    public void LoadsAModuleNamedByItsGeneratedAttribute(IDependencyOne dependencyOne) {
        Assert.That(dependencyOne.SingletonService, Is.Not.Null);
        Assert.That(dependencyOne.ScopedService, Is.Not.Null);
    }

    [ModuleTest(typeof(SutModule), typeof(ExtraModule))]
    public void LoadsSeveralModules(ISingletonService singletonService, ExtraService extraService) {
        Assert.That(singletonService, Is.Not.Null);
        Assert.That(extraService, Is.Not.Null);
    }

    [ModuleTest]
    public void TakesNoModulesAtAll() {
        Assert.Pass("a module test need not name a module");
    }

    /// <summary>The container itself, which cannot be resolved from itself.</summary>
    [ModuleTest(typeof(SutModule))]
    public void InjectsTheServiceProvider(IServiceProvider serviceProvider) {
        Assert.That(serviceProvider.GetService<ISingletonService>(), Is.Not.Null);
    }

    /// <summary>
    /// An unregistered concrete type the container can still build, which is how a test names the
    /// class under test without registering it.
    /// </summary>
    [ModuleTest(typeof(SutModule))]
    public void ConstructsAnUnregisteredConcreteType(NeedsASingleton needsASingleton) {
        Assert.That(needsASingleton.SingletonService, Is.Not.Null);
    }

    [ModuleTest(typeof(SutModule))]
    public void PublishesTheTestCaseInfo(ITestCaseInfo testCaseInfo, ISingletonService singletonService) {
        Assert.That(testCaseInfo.TestMethod.Name, Is.EqualTo(nameof(PublishesTheTestCaseInfo)));
        Assert.That(testCaseInfo.TestMethodArguments, Has.Count.EqualTo(2));
        Assert.That(testCaseInfo.TestMethodArguments[1], Is.SameAs(singletonService));
    }

    public class NeedsASingleton(ISingletonService singletonService) {
        public ISingletonService SingletonService { get; } = singletonService;
    }
}

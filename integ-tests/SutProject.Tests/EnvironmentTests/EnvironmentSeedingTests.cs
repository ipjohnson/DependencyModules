using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.EnvironmentTests;

/// <summary>
/// What an integration's environment attribute looks like: name the environment, hand it over.
/// Pinned away from process variables so a machine's ASPNETCORE_ENVIRONMENT cannot reach these
/// tests.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class SeededEnvironmentAttribute(string name) : Attribute, IModuleEnvironmentProvider {
    public IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod) =>
        new ModuleEnvironment(false, name);
}

public interface IGatedByEnvironment { }

[SingletonService(Realm = typeof(SeededEnvironmentModule))]
[IfEnvironment("seeded-environment")]
public class GatedByEnvironment : IGatedByEnvironment { }

[DependencyModule(OnlyRealm = true)]
public partial class SeededEnvironmentModule { }

/// <summary>
/// The seeded environment reaches module conditions through the real test runner.
/// </summary>
/// <remarks>
/// <see cref="EnvironmentConfigurationTests"/> proves the same behaviour for a hand-built
/// collection through <c>AddModules</c>. These run through <c>[ModuleTest]</c> itself, which is
/// the path that had no way to supply an environment at all: modules are applied before the
/// service-setup pass, so every condition had been decided against the process default before an
/// attribute could register anything.
/// </remarks>
public class EnvironmentSeedingTests {

    [ModuleTest]
    [SeededEnvironmentModule]
    [SeededEnvironment("seeded-environment")]
    public void AGatedRegistrationAppliesUnderTheSeededEnvironment(IServiceProvider provider) {
        Assert.NotNull(provider.GetService<IGatedByEnvironment>());
    }

    /// <summary>
    /// The gate has to hold in the other direction, or the test above passes because the
    /// condition was never compiled in.
    /// </summary>
    [ModuleTest]
    [SeededEnvironmentModule]
    public void TheSameRegistrationIsAbsentWithoutASeed(IServiceProvider provider) {
        Assert.Null(provider.GetService<IGatedByEnvironment>());
    }

    /// <summary>The environment a module reads is the seeded instance, not a parallel default.</summary>
    [ModuleTest]
    [EnvironmentAwareModule]
    [SeededEnvironment("seeded-environment")]
    public void AModuleReadingTheEnvironmentSeesTheSeededOne(IEnvironmentDependency dependency) {
        Assert.Equal("seeded-environment", dependency.EnvironmentName);
    }

    [ModuleTest]
    [EnvironmentAwareModule]
    public void WithoutASeedTheProcessDefaultApplies(IEnvironmentDependency dependency) {
        Assert.Equal(ModuleEnvironment.CreateDefault().EnvironmentName, dependency.EnvironmentName);
    }
}

/// <summary>Narrowest scope wins, matching how every other attribute here resolves.</summary>
[SeededEnvironment("outer-environment")]
public class EnvironmentSeedingPrecedenceTests {

    [ModuleTest]
    [EnvironmentAwareModule]
    public void AClassLevelSeedApplies(IEnvironmentDependency dependency) {
        Assert.Equal("outer-environment", dependency.EnvironmentName);
    }

    [ModuleTest]
    [EnvironmentAwareModule]
    [SeededEnvironment("inner-environment")]
    public void TheMethodsSeedBeatsTheClasses(IEnvironmentDependency dependency) {
        Assert.Equal("inner-environment", dependency.EnvironmentName);
    }
}

using System.Reflection;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace SutProject.NUnitTests;

/// <summary>
/// The NUnit twin of the xUnit environment-seeding tests: the seeded environment reaches module
/// conditions through the real runner, which applies modules before the service-setup pass.
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

public class EnvironmentSeedingTests {

    [ModuleTest(typeof(SeededEnvironmentModule))]
    [SeededEnvironment("seeded-environment")]
    public void AGatedRegistrationAppliesUnderTheSeededEnvironment(IServiceProvider provider) {
        Assert.That(provider.GetService<IGatedByEnvironment>(), Is.Not.Null);
    }

    /// <summary>
    /// The gate has to hold in the other direction, or the test above passes because the
    /// condition was never compiled in.
    /// </summary>
    [ModuleTest(typeof(SeededEnvironmentModule))]
    public void TheSameRegistrationIsAbsentWithoutASeed(IServiceProvider provider) {
        Assert.That(provider.GetService<IGatedByEnvironment>(), Is.Null);
    }
}

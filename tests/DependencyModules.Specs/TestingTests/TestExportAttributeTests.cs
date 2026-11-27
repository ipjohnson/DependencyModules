using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.TestingTests;

/// <summary>
/// [TestExport] lets a test override a registration for the duration of that test, so the lifetime
/// and implementation it produces have to match what was asked for.
/// </summary>
public partial class TestExportAttributeTests
{
    private interface IThing;

    private class Thing : IThing;

    private class OtherThing : IThing;

    [VerifyTest]
    public void DefaultsToTransient()
    {
        Verify(new TestExportAttribute(typeof(IThing)).Lifetime == ServiceLifetime.Transient);
    }

    [VerifyTest]
    public void DefaultsToNoSeparateImplementation()
    {
        Verify(new TestExportAttribute(typeof(IThing)).Implementation is null);
    }

    [VerifyTest]
    public void ExposesTheServiceItWasGiven()
    {
        Verify(new TestExportAttribute(typeof(IThing)).Service == typeof(IThing));
    }

    [VerifyTest]
    [Row(ServiceLifetime.Singleton)]
    [Row(ServiceLifetime.Scoped)]
    [Row(ServiceLifetime.Transient)]
    public void RegistersWithTheRequestedLifetime(ServiceLifetime lifetime)
    {
        var attribute = new TestExportAttribute(typeof(IThing))
        {
            Implementation = typeof(Thing),
            Lifetime = lifetime,
        };

        var collection = Setup(attribute);

        var descriptor = Single(collection);
        Verify(descriptor.Lifetime == lifetime);
        Verify(descriptor.ServiceType == typeof(IThing));
        Verify(descriptor.ImplementationType == typeof(Thing));
    }

    [VerifyTest]
    public void WithoutAnImplementation_RegistersTheServiceAsItsOwnImplementation()
    {
        var collection = Setup(new TestExportAttribute(typeof(Thing)));

        var descriptor = Single(collection);
        Verify(descriptor.ServiceType == typeof(Thing));
        Verify(descriptor.ImplementationType == typeof(Thing));
    }

    [VerifyTest]
    public void RegisteredServiceResolvesFromTheProvider()
    {
        var collection = Setup(
            new TestExportAttribute(typeof(IThing))
            {
                Implementation = typeof(OtherThing),
                Lifetime = ServiceLifetime.Singleton,
            }
        );

        var provider = collection.BuildServiceProvider();

        Verify(provider.GetService<IThing>()?.GetType() == typeof(OtherThing));
    }

    /// <summary>
    /// [TestExport] only ever registers, so it implements the setup hook and not the lifecycle one.
    /// It used to carry a no-op StartupAsync purely because the two lived on one interface; pinning
    /// the split here means re-adding a lifecycle hook has to be a decision rather than an accident.
    /// </summary>
    [VerifyTest]
    public void RegistersServicesWithoutTakingPartInTestStartup()
    {
        object attribute = new TestExportAttribute(typeof(IThing));

        Verify(attribute is ITestServiceSetupAttribute);
        Verify(attribute is not ITestStartupAttribute);
    }

    [VerifyTest]
    public void AppliesToAssembliesClassesAndMethods()
    {
        var usage = typeof(TestExportAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Verify(usage.ValidOn.HasFlag(AttributeTargets.Assembly));
        Verify(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Verify(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Verify(usage.AllowMultiple);
    }

    /// <summary>
    /// SetupServiceCollection does not read the test method, so tests supply none.
    /// </summary>
    private static IServiceCollection Setup(TestExportAttribute attribute)
    {
        var collection = new ServiceCollection();
        attribute.SetupServiceCollection(null!, collection);
        return collection;
    }
}

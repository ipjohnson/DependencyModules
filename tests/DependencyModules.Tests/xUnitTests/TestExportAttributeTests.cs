using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.xUnitTests;

/// <summary>
/// [TestExport] lets a test override a registration for the duration of that test, so the lifetime
/// and implementation it produces have to match what was asked for.
/// </summary>
public class TestExportAttributeTests {

    private interface IThing;

    private class Thing : IThing;

    private class OtherThing : IThing;

    [Fact]
    public void DefaultsToTransient() {
        Assert.Equal(ServiceLifetime.Transient, new TestExportAttribute(typeof(IThing)).Lifetime);
    }

    [Fact]
    public void DefaultsToNoSeparateImplementation() {
        Assert.Null(new TestExportAttribute(typeof(IThing)).Implementation);
    }

    [Fact]
    public void ExposesTheServiceItWasGiven() {
        Assert.Equal(typeof(IThing), new TestExportAttribute(typeof(IThing)).Service);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void RegistersWithTheRequestedLifetime(ServiceLifetime lifetime) {
        var attribute = new TestExportAttribute(typeof(IThing)) {
            Implementation = typeof(Thing),
            Lifetime = lifetime
        };

        var collection = Setup(attribute);

        var descriptor = Assert.Single(collection);
        Assert.Equal(lifetime, descriptor.Lifetime);
        Assert.Equal(typeof(IThing), descriptor.ServiceType);
        Assert.Equal(typeof(Thing), descriptor.ImplementationType);
    }

    [Fact]
    public void WithoutAnImplementation_RegistersTheServiceAsItsOwnImplementation() {
        var collection = Setup(new TestExportAttribute(typeof(Thing)));

        var descriptor = Assert.Single(collection);
        Assert.Equal(typeof(Thing), descriptor.ServiceType);
        Assert.Equal(typeof(Thing), descriptor.ImplementationType);
    }

    [Fact]
    public void RegisteredServiceResolvesFromTheProvider() {
        var collection = Setup(new TestExportAttribute(typeof(IThing)) {
            Implementation = typeof(OtherThing),
            Lifetime = ServiceLifetime.Singleton
        });

        var provider = collection.BuildServiceProvider();

        Assert.IsType<OtherThing>(provider.GetService<IThing>());
    }

    [Fact]
    public void StartupAsync_CompletesWithoutWork() {
        var task = new TestExportAttribute(typeof(IThing)).StartupAsync(null!, new ServiceCollection().BuildServiceProvider());

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void AppliesToAssembliesClassesAndMethods() {
        var usage = typeof(TestExportAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Assembly));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Assert.True(usage.AllowMultiple);
    }

    /// <summary>
    /// SetupServiceCollection does not read the test method, so tests supply none.
    /// </summary>
    private static IServiceCollection Setup(TestExportAttribute attribute) {
        var collection = new ServiceCollection();
        attribute.SetupServiceCollection(null!, collection);
        return collection;
    }
}

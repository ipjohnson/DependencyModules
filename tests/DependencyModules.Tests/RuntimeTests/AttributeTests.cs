using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// The service attributes are read by the generator at compile time, but they are also public API:
/// their property surface and attribute targets are part of the 1.0 contract.
/// </summary>
public class AttributeTests {

    private interface IThing;

    [Fact]
    public void SingletonService_ReportsSingletonLifetime() {
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(new SingletonServiceAttribute()));
    }

    [Fact]
    public void ScopedService_ReportsScopedLifetime() {
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf(new ScopedServiceAttribute()));
    }

    [Fact]
    public void TransientService_ReportsTransientLifetime() {
        Assert.Equal(ServiceLifetime.Transient, LifetimeOf(new TransientServiceAttribute()));
    }

    [Fact]
    public void SettingLifetimeThroughTheInterface_IsRejected() {
        IServiceRegistrationAttribute attribute = new SingletonServiceAttribute();

        var exception = Assert.Throws<Exception>(() => attribute.Lifetime = ServiceLifetime.Scoped);

        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void ServiceAttribute_DefaultsToAddRegistration() {
        Assert.Equal(RegistrationType.Add, new SingletonServiceAttribute().Using);
    }

    [Fact]
    public void ServiceAttribute_RoundTripsItsProperties() {
        var attribute = new SingletonServiceAttribute {
            Key = "the-key",
            As = typeof(IThing),
            Using = RegistrationType.Try,
            Realm = typeof(AttributeTests)
        };

        Assert.Equal("the-key", attribute.Key);
        Assert.Equal(typeof(IThing), attribute.As);
        Assert.Equal(RegistrationType.Try, attribute.Using);
        Assert.Equal(typeof(AttributeTests), attribute.Realm);
    }

    [Fact]
    public void ServiceAttribute_DefaultsItsOptionalPropertiesToNull() {
        var attribute = new TransientServiceAttribute();

        Assert.Null(attribute.Key);
        Assert.Null(attribute.As);
        Assert.Null(attribute.Realm);
    }

    [Fact]
    public void DependencyModuleAttribute_HasTheDocumentedDefaults() {
        var attribute = new DependencyModuleAttribute();

        Assert.False(attribute.OnlyRealm);
        Assert.Equal(RegistrationType.Add, attribute.Using);
        Assert.True(attribute.GenerateAttribute);
        Assert.False(attribute.RegisterJsonSerializers);
        Assert.False(attribute.GenerateFactories);
        Assert.Null(attribute.GenerateUseMethod);
    }

    [Fact]
    public void DependencyModuleAttribute_RoundTripsItsProperties() {
        var attribute = new DependencyModuleAttribute {
            OnlyRealm = true,
            Using = RegistrationType.Replace,
            GenerateAttribute = false,
            RegisterJsonSerializers = true,
            GenerateFactories = true,
            GenerateUseMethod = "UseThing"
        };

        Assert.True(attribute.OnlyRealm);
        Assert.Equal(RegistrationType.Replace, attribute.Using);
        Assert.False(attribute.GenerateAttribute);
        Assert.True(attribute.RegisterJsonSerializers);
        Assert.True(attribute.GenerateFactories);
        Assert.Equal("UseThing", attribute.GenerateUseMethod);
    }

    [Theory]
    [InlineData(typeof(SingletonServiceAttribute))]
    [InlineData(typeof(ScopedServiceAttribute))]
    [InlineData(typeof(TransientServiceAttribute))]
    [InlineData(typeof(CrossWireServiceAttribute))]
    public void ServiceAttributes_TargetClassesAndMethods(Type attributeType) {
        var usage = attributeType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void DependencyModuleAttribute_TargetsClassesAndAssemblies() {
        var usage = typeof(DependencyModuleAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Assembly));
        Assert.False(usage.Inherited);
    }

    private static ServiceLifetime LifetimeOf(IServiceRegistrationAttribute attribute) => attribute.Lifetime;
}

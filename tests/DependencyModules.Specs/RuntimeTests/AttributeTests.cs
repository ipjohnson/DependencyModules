using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

/// <summary>
/// The service attributes are read by the generator at compile time, but they are also public API:
/// their property surface and attribute targets are part of the 1.0 contract.
/// </summary>
public partial class AttributeTests
{
    private interface IThing;

    [VerifyTest]
    public void SingletonService_ReportsSingletonLifetime()
    {
        Verify(LifetimeOf(new SingletonServiceAttribute()) == ServiceLifetime.Singleton);
    }

    [VerifyTest]
    public void ScopedService_ReportsScopedLifetime()
    {
        Verify(LifetimeOf(new ScopedServiceAttribute()) == ServiceLifetime.Scoped);
    }

    [VerifyTest]
    public void TransientService_ReportsTransientLifetime()
    {
        Verify(LifetimeOf(new TransientServiceAttribute()) == ServiceLifetime.Transient);
    }

    [VerifyTest]
    public void SettingLifetimeThroughTheInterface_IsRejected()
    {
        IServiceRegistrationAttribute attribute = new SingletonServiceAttribute();

        var exception = ThrowsExactly<Exception>(() => attribute.Lifetime = ServiceLifetime.Scoped);

        Verify(exception.Message.Contains("not supported"));
    }

    [VerifyTest]
    public void ServiceAttribute_DefaultsToAddRegistration()
    {
        Verify(new SingletonServiceAttribute().Using == RegistrationType.Add);
    }

    [VerifyTest]
    public void ServiceAttribute_RoundTripsItsProperties()
    {
        var attribute = new SingletonServiceAttribute
        {
            Key = "the-key",
            As = typeof(IThing),
            Using = RegistrationType.Try,
            Realm = typeof(AttributeTests),
        };

        Verify(Equals(attribute.Key, "the-key"));
        Verify(attribute.As == typeof(IThing));
        Verify(attribute.Using == RegistrationType.Try);
        Verify(attribute.Realm == typeof(AttributeTests));
    }

    [VerifyTest]
    public void ServiceAttribute_DefaultsItsOptionalPropertiesToNull()
    {
        var attribute = new TransientServiceAttribute();

        Verify(attribute.Key is null);
        Verify(attribute.As is null);
        Verify(attribute.Realm is null);
    }

    [VerifyTest]
    public void DependencyModuleAttribute_HasTheDocumentedDefaults()
    {
        var attribute = new DependencyModuleAttribute();

        Verify(!attribute.OnlyRealm);
        Verify(attribute.Using == RegistrationType.Add);
        Verify(attribute.GenerateAttribute);
        Verify(!attribute.RegisterJsonSerializers);
        Verify(!attribute.GenerateFactories);
        Verify(attribute.GenerateUseMethod is null);
    }

    [VerifyTest]
    public void DependencyModuleAttribute_RoundTripsItsProperties()
    {
        var attribute = new DependencyModuleAttribute
        {
            OnlyRealm = true,
            Using = RegistrationType.Replace,
            GenerateAttribute = false,
            RegisterJsonSerializers = true,
            GenerateFactories = true,
            GenerateUseMethod = "UseThing",
        };

        Verify(attribute.OnlyRealm);
        Verify(attribute.Using == RegistrationType.Replace);
        Verify(!attribute.GenerateAttribute);
        Verify(attribute.RegisterJsonSerializers);
        Verify(attribute.GenerateFactories);
        Verify(attribute.GenerateUseMethod == "UseThing");
    }

    [VerifyTest]
    [Row(typeof(SingletonServiceAttribute))]
    [Row(typeof(ScopedServiceAttribute))]
    [Row(typeof(TransientServiceAttribute))]
    [Row(typeof(CrossWireServiceAttribute))]
    public void ServiceAttributes_TargetClassesAndMethods(Type attributeType)
    {
        var usage = attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Verify(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Verify(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Verify(usage.AllowMultiple);
        Verify(!usage.Inherited);
    }

    [VerifyTest]
    public void DependencyModuleAttribute_TargetsClassesAndAssemblies()
    {
        var usage = typeof(DependencyModuleAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Verify(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Verify(usage.ValidOn.HasFlag(AttributeTargets.Assembly));
        Verify(!usage.Inherited);
    }

    private static ServiceLifetime LifetimeOf(IServiceRegistrationAttribute attribute) =>
        attribute.Lifetime;
}

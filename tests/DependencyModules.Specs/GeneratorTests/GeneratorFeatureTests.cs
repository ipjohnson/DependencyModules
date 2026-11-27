using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Covers the documented module features end to end: realms, use methods, decorators, feature
/// handlers, factories, and module composition. Each asserts that the generator produces output
/// that compiles, which is the property most easily broken by a change to the writers.
/// </summary>
public partial class GeneratorFeatureTests
{
    [VerifyTest]
    public void OnlyRealm_RegistersOnlyServicesMarkedForThatRealm()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IInRealm;
            public interface IOutsideRealm;

            [DependencyModule(OnlyRealm = true)]
            public partial class RealmModule;

            [SingletonService(Realm = typeof(RealmModule))]
            public class InRealm : IInRealm;

            [SingletonService]
            public class OutsideRealm : IOutsideRealm;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("InRealm"));
        Verify(!generated.Contains("OutsideRealm"));
    }

    [VerifyTest]
    public void ModuleWithoutOnlyRealm_RegistersUnmarkedServices()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class OpenModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("Thing"));
    }

    [VerifyTest]
    public void GenerateUseMethod_EmitsTheNamedMethod()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule(GenerateUseMethod = "UseTestModule", OnlyRealm = true)]
            public partial class UseMethodModule(string name) {
                public string Name => name;
            }

            [SingletonService(Realm = typeof(UseMethodModule))]
            public class RealmService;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining(".Module.g.cs").Contains("UseTestModule"));
    }

    [VerifyTest]
    public void GenerateAttributeFalse_SuppressesTheModuleAttribute()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule(GenerateAttribute = false)]
            public partial class NoAttributeModule;
            """
        );

        result.AssertNoErrors();
        Verify(
            !result.SourceContaining(".Module.g.cs").Contains("class NoAttributeModuleAttribute")
        );
    }

    [VerifyTest]
    public void ModuleImplementingAFeature_EmitsAFeatureApplicator()
    {
        var result = GeneratorTestHarness.Run(
            """
            using System.Collections.Generic;
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Features;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IModuleFeatureValue {
                string Value { get; }
            }

            [DependencyModule(OnlyRealm = true)]
            public partial class FeatureHandlerModule : IDependencyModuleFeature<IModuleFeatureValue> {
                public void HandleFeature(IServiceCollection collection, IEnumerable<IModuleFeatureValue> feature) {
                }
            }
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Verify(generated.Contains("FeatureApplicator"));
        Verify(generated.Contains("IModuleFeatureValue"));
    }

    [VerifyTest]
    public void ModuleImplementingConfiguration_StillGeneratesRegistrations()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interfaces;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class ConfiguringModule : IServiceCollectionConfiguration {
                public void ConfigureServices(IServiceCollection services) {
                }

                public void ConfigureDecorators(IServiceCollection services) {
                }
            }
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("AddSingleton"));
    }

    [VerifyTest]
    public void ModuleWithConstructorParameters_PutsThemOnTheGeneratedAttribute()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class ParameterizedModule(bool someFlag, string name);
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Verify(generated.Contains("someFlag"));
        Verify(generated.Contains("name"));
    }

    [VerifyTest]
    public void ModuleWithSettableProperties_PutsThemOnTheGeneratedAttribute()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class PropertyModule {
                public string Settable { get; set; } = "";
                public string ReadOnly { get; } = "";
            }
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Verify(generated.Contains("Settable"));
        Verify(!generated.Contains("public string ReadOnly"));
    }

    [VerifyTest]
    public void ModuleApplyingAnotherModule_CompilesAndReferencesIt()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class BaseModule;

            [DependencyModule]
            [BaseModule]
            public partial class ComposedModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("ComposedModule.Module").Contains("BaseModuleAttribute"));
    }

    [VerifyTest]
    public void FactoryWithParameters_EmitsAFactoryRegistration()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IDependency;
            public interface IProduct;

            [SingletonService]
            public class Dependency : IDependency;

            public class Product : IProduct {
                private Product() { }

                [SingletonService]
                public static IProduct Create(IDependency dependency) => new Product();
            }

            [DependencyModule]
            public partial class FactoryModule;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("Create"));
        Verify(generated.Contains("IDependency"));
    }

    [VerifyTest]
    public void ServiceWithConstructorDependencies_Compiles()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IFirst;
            public interface ISecond;

            [SingletonService]
            public class First : IFirst;

            [SingletonService]
            public class Second : ISecond {
                public Second(IFirst first, string name = "default") { }
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("Second"));
    }

    /// <summary>
    /// Auto-registration picks the first implemented interface. Registering every interface is
    /// what [CrossWireService] is for, and [SingletonService(As = ...)] selects a specific one.
    /// </summary>
    [VerifyTest]
    public void ServiceImplementingSeveralInterfaces_RegistersTheFirstOne()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IFirst;
            public interface ISecond;

            [SingletonService]
            public class Both : IFirst, ISecond;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("IFirst"));
        Verify(!generated.Contains("ISecond"));
    }

    [VerifyTest]
    public void CrossWireService_RegistersTheImplementationAndItsInterfaces()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IFirst;
            public interface ISecond;

            [CrossWireService]
            public class Both : IFirst, ISecond;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("Both"));
        Verify(generated.Contains("IFirst"));
        Verify(generated.Contains("ISecond"));
    }

    [VerifyTest]
    public void ModuleLevelRegistrationType_AppliesToItsServices()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule(Using = RegistrationType.Try)]
            public partial class TryModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("TryAdd"));
    }

    [VerifyTest]
    public void SeveralModulesInOneCompilation_EachGetTheirOwnFiles()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class FirstModule;

            [DependencyModule]
            public partial class SecondModule;
            """
        );

        result.AssertNoErrors();

        Verify(result.GeneratedSources.Keys.Any(key => key.StartsWith("FirstModule.")));
        Verify(result.GeneratedSources.Keys.Any(key => key.StartsWith("SecondModule.")));
    }

    [VerifyTest]
    public void ModuleInANestedNamespace_GeneratesIntoThatNamespace()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace Outer.Inner;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class NestedModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining(".Module.g.cs").Contains("namespace Outer.Inner"));
    }

    [VerifyTest]
    public void ServiceWithNoInterfaces_RegistersItselfAsTheServiceType()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [SingletonService]
            public class Standalone;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("Standalone"));
    }

    [VerifyTest]
    public void ModuleThatOverridesEquals_KeepsTheDeveloperImplementation()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class CustomEqualsModule(string key) {
                private string Key { get; } = key;

                public override bool Equals(object? obj) => obj is CustomEqualsModule other && other.Key == Key;

                public override int GetHashCode() => Key.GetHashCode();
            }
            """
        );

        result.AssertNoErrors();
        Verify(!result.SourceContaining(".Module.g.cs").Contains("public override bool Equals"));
    }

    [VerifyTest]
    public void ScopedAndTransientFactories_AreSupported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IScopedThing;
            public interface ITransientThing;

            public class Factories {
                [ScopedService]
                public static IScopedThing CreateScoped() => null!;

                [TransientService]
                public static ITransientThing CreateTransient() => null!;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("AddScoped"));
        Verify(generated.Contains("AddTransient"));
    }
}

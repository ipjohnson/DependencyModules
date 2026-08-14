using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Compiles source with the generator, loads the emitted assembly, and asserts on what the
/// registrations actually do at run time: which implementation is resolved, with which lifetime,
/// and whether instances are shared.
///
/// These are the tests that would fail if the generator emitted well-formed code that registered
/// the wrong thing. Asserting on the shape of generated text cannot catch that.
/// </summary>
public class GeneratedBehaviourTests {

    [Fact]
    public void SingletonService_ResolvesTheImplementation() {
        var generated = GeneratedAssembly.Create(Module("[SingletonService] public class Thing : IThing;"));

        var resolved = generated.ResolveRequired("IThing");

        Assert.Equal(generated.Type("Thing"), resolved.GetType());
    }

    [Fact]
    public void SingletonService_ReturnsTheSameInstanceEveryTime() {
        var generated = GeneratedAssembly.Create(Module("[SingletonService] public class Thing : IThing;"));
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Assert.Same(provider.GetService(serviceType), provider.GetService(serviceType));
    }

    [Fact]
    public void TransientService_ReturnsANewInstanceEveryTime() {
        var generated = GeneratedAssembly.Create(Module("[TransientService] public class Thing : IThing;"));
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Assert.NotSame(provider.GetService(serviceType), provider.GetService(serviceType));
    }

    [Fact]
    public void ScopedService_IsSharedWithinAScopeAndDiffersAcrossScopes() {
        var generated = GeneratedAssembly.Create(Module("[ScopedService] public class Thing : IThing;"));
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var firstA = first.ServiceProvider.GetService(serviceType);
        var firstB = first.ServiceProvider.GetService(serviceType);
        var secondA = second.ServiceProvider.GetService(serviceType);

        Assert.Same(firstA, firstB);
        Assert.NotSame(firstA, secondA);
    }

    [Theory]
    [InlineData("SingletonService", ServiceLifetime.Singleton)]
    [InlineData("ScopedService", ServiceLifetime.Scoped)]
    [InlineData("TransientService", ServiceLifetime.Transient)]
    public void ServiceAttribute_RegistersTheMatchingLifetime(string attribute, ServiceLifetime expected) {
        var generated = GeneratedAssembly.Create(Module($"[{attribute}] public class Thing : IThing;"));

        Assert.Equal(expected, generated.Descriptor("IThing").Lifetime);
    }

    [Fact]
    public void AsProperty_ResolvesUnderTheRequestedServiceTypeOnly() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public interface IOther;
            [SingletonService(As = typeof(IOther))] public class Thing : IThing, IOther;
            """));

        var provider = generated.BuildProvider();

        Assert.NotNull(provider.GetService(generated.Type("IOther")));
        Assert.Null(provider.GetService(generated.Type("IThing")));
    }

    [Fact]
    public void KeyedService_ResolvesOnlyThroughItsKey() {
        var generated = GeneratedAssembly.Create(Module(
            """[SingletonService(Key = "the-key")] public class Thing : IThing;"""));

        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Assert.NotNull(provider.GetKeyedService(serviceType, "the-key"));
        Assert.Null(provider.GetService(serviceType));
    }

    [Fact]
    public void KeyedServices_WithDifferentKeysResolveDifferentImplementations() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [SingletonService(Key = "first")] public class FirstThing : IThing;
            [SingletonService(Key = "second")] public class SecondThing : IThing;
            """));

        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Assert.Equal(generated.Type("FirstThing"), provider.GetKeyedService(serviceType, "first")!.GetType());
        Assert.Equal(generated.Type("SecondThing"), provider.GetKeyedService(serviceType, "second")!.GetType());
    }

    /// <summary>
    /// The contract of [CrossWireService]: one instance, reachable through the implementation type
    /// and through every interface it implements.
    /// </summary>
    [Fact]
    public void CrossWireService_SharesOneInstanceAcrossAllItsInterfaces() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public interface IOther;
            [CrossWireService(Lifetime = ServiceLifetime.Singleton)]
            public class Thing : IThing, IOther;
            """,
            extraUsings: "using Microsoft.Extensions.DependencyInjection;"));

        var provider = generated.BuildProvider();

        var asThing = provider.GetService(generated.Type("IThing"));
        var asOther = provider.GetService(generated.Type("IOther"));
        var asImplementation = provider.GetService(generated.Type("Thing"));

        Assert.NotNull(asThing);
        Assert.Same(asThing, asOther);
        Assert.Same(asThing, asImplementation);
    }

    [Fact]
    public void TryRegistration_DoesNotReplaceAnExistingRegistration() {
        var generated = GeneratedAssembly.Create(Module(
            "[SingletonService(Using = RegistrationType.Try)] public class Thing : IThing;"));

        Assert.Equal(ServiceLifetime.Singleton, generated.Descriptor("IThing").Lifetime);
        Assert.Equal(generated.Type("Thing"), generated.ResolveRequired("IThing").GetType());
    }

    [Fact]
    public void ReplaceRegistration_LeavesASingleRegistration() {
        var generated = GeneratedAssembly.Create(Module(
            "[SingletonService(Using = RegistrationType.Replace)] public class Thing : IThing;"));

        Assert.Single(generated.Descriptors("IThing"));
    }

    /// <summary>
    /// Registrations within a module are emitted sorted by implementation type name, and both
    /// Replace and Try act on a registration that has to already be there. Named so that the
    /// alphabet puts them first, they used to run before their target existed: Replace replaced
    /// nothing and added itself, then the registration it meant to displace was added after it and
    /// won. Renaming the class fixed it, and nothing said so.
    /// </summary>
    [Fact]
    public void ReplaceRegistration_WinsEvenWhenItsTypeNameSortsFirst() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [SingletonService(Using = RegistrationType.Replace)] public class AaaThing : IThing;
            [SingletonService] public class ZzzThing : IThing;
            """));

        Assert.Single(generated.Descriptors("IThing"));
        Assert.Equal(generated.Type("AaaThing"), generated.ResolveRequired("IThing").GetType());
    }

    [Fact]
    public void TryRegistration_DeclinesEvenWhenItsTypeNameSortsFirst() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [SingletonService(Using = RegistrationType.Try)] public class AaaThing : IThing;
            [SingletonService] public class ZzzThing : IThing;
            """));

        Assert.Single(generated.Descriptors("IThing"));
        Assert.Equal(generated.Type("ZzzThing"), generated.ResolveRequired("IThing").GetType());
    }

    [Fact]
    public void ConstructorDependencies_AreInjectedFromTheContainer() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public interface IDependency;

            [SingletonService] public class Dependency : IDependency;

            [SingletonService]
            public class Thing : IThing {
                public IDependency Injected { get; }
                public Thing(IDependency dependency) => Injected = dependency;
            }
            """));

        var provider = generated.BuildProvider();
        var thing = provider.GetService(generated.Type("IThing"))!;

        var injected = thing.GetType().GetProperty("Injected")!.GetValue(thing);

        Assert.NotNull(injected);
        Assert.Same(provider.GetService(generated.Type("IDependency")), injected);
    }

    [Fact]
    public void StaticFactory_IsInvokedToCreateTheService() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public class Thing : IThing {
                public string Origin { get; private set; } = "constructor";

                [SingletonService]
                public static IThing Create() => new Thing { Origin = "factory" };
            }
            """));

        var thing = generated.ResolveRequired("IThing");

        Assert.Equal("factory", thing.GetType().GetProperty("Origin")!.GetValue(thing));
    }

    [Fact]
    public void StaticFactory_ReceivesItsDependenciesFromTheContainer() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public interface IDependency;

            [SingletonService] public class Dependency : IDependency;

            public class Thing : IThing {
                public IDependency? Injected { get; private set; }

                [SingletonService]
                public static IThing Create(IDependency dependency) => new Thing { Injected = dependency };
            }
            """));

        var provider = generated.BuildProvider();
        var thing = provider.GetService(generated.Type("IThing"))!;

        Assert.Same(
            provider.GetService(generated.Type("IDependency")),
            thing.GetType().GetProperty("Injected")!.GetValue(thing));
    }

    [Fact]
    public void OpenGenericService_ResolvesForAnyTypeArgument() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class GenericThing<T> : IGeneric<T>;

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();
        var closed = generated.Type("IGeneric`1").MakeGenericType(typeof(string));

        Assert.NotNull(provider.GetService(closed));
    }

    [Fact]
    public void ClosedGenericService_ResolvesForItsOwnArgumentOnly() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class StringGeneric : IGeneric<string>;

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();
        var generic = generated.Type("IGeneric`1");

        Assert.NotNull(provider.GetService(generic.MakeGenericType(typeof(string))));
        Assert.Null(provider.GetService(generic.MakeGenericType(typeof(int))));
    }

    [Fact]
    public void NestedService_ResolvesThroughItsContainingType() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public static class Outer {
                [SingletonService]
                public class Inner : IThing;
            }
            """));

        Assert.Equal("Inner", generated.ResolveRequired("IThing").GetType().Name);
    }

    [Fact]
    public void RecordService_Resolves() {
        var generated = GeneratedAssembly.Create(Module("[SingletonService] public record ThingRecord : IThing;"));

        Assert.Equal(generated.Type("ThingRecord"), generated.ResolveRequired("IThing").GetType());
    }

    [Fact]
    public void ModuleConfiguration_RunsAlongsideGeneratedRegistrations() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interfaces;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IThing;
            public interface IManual;

            [SingletonService] public class Thing : IThing;

            public class Manual : IManual;

            [DependencyModule]
            public partial class TestModule : IServiceCollectionConfiguration {
                public void ConfigureServices(IServiceCollection services) {
                    services.AddSingleton<IManual, Manual>();
                }
            }
            """);

        var provider = generated.BuildProvider();

        Assert.NotNull(provider.GetService(generated.Type("IThing")));
        Assert.NotNull(provider.GetService(generated.Type("IManual")));
    }

    [Fact]
    public void OnlyRealm_RegistersOnlyServicesInThatRealm() {
        var generated = GeneratedAssembly.Create(
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
            """,
            "RealmModule");

        var provider = generated.BuildProvider();

        Assert.NotNull(provider.GetService(generated.Type("IInRealm")));
        Assert.Null(provider.GetService(generated.Type("IOutsideRealm")));
    }

    [Fact]
    public void ComposedModule_AppliesTheModulesItReferences() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IFromBase;

            [DependencyModule(OnlyRealm = true)]
            public partial class BaseModule;

            [SingletonService(Realm = typeof(BaseModule))]
            public class FromBase : IFromBase;

            [DependencyModule(OnlyRealm = true)]
            [BaseModule]
            public partial class ComposedModule;
            """,
            "ComposedModule");

        Assert.NotNull(generated.BuildProvider().GetService(generated.Type("IFromBase")));
    }

    [Fact]
    public void ServiceWithNoInterface_ResolvesAsItself() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [SingletonService]
            public class Standalone;

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.NotNull(generated.BuildProvider().GetService(generated.Type("Standalone")));
    }

    private static string Module(string body, string extraUsings = "") =>
        $$"""
          using DependencyModules.Runtime.Attributes;
          {{extraUsings}}

          namespace TestNamespace;

          public interface IThing;

          {{body}}

          [DependencyModule]
          public partial class TestModule;
          """;
}

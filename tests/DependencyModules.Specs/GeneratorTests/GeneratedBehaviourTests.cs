using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Compiles source with the generator, loads the emitted assembly, and asserts on what the
/// registrations actually do at run time: which implementation is resolved, with which lifetime,
/// and whether instances are shared.
///
/// These are the tests that would fail if the generator emitted well-formed code that registered
/// the wrong thing. Asserting on the shape of generated text cannot catch that.
/// </summary>
public partial class GeneratedBehaviourTests
{
    [VerifyTest]
    public void SingletonService_ResolvesTheImplementation()
    {
        var generated = GeneratedAssembly.Create(
            Module("[SingletonService] public class Thing : IThing;")
        );

        var resolved = generated.ResolveRequired("IThing");

        Verify(resolved.GetType() == generated.Type("Thing"));
    }

    [VerifyTest]
    public void SingletonService_ReturnsTheSameInstanceEveryTime()
    {
        var generated = GeneratedAssembly.Create(
            Module("[SingletonService] public class Thing : IThing;")
        );
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Verify(ReferenceEquals(provider.GetService(serviceType), provider.GetService(serviceType)));
    }

    [VerifyTest]
    public void TransientService_ReturnsANewInstanceEveryTime()
    {
        var generated = GeneratedAssembly.Create(
            Module("[TransientService] public class Thing : IThing;")
        );
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Verify(
            !ReferenceEquals(provider.GetService(serviceType), provider.GetService(serviceType))
        );
    }

    [VerifyTest]
    public void ScopedService_IsSharedWithinAScopeAndDiffersAcrossScopes()
    {
        var generated = GeneratedAssembly.Create(
            Module("[ScopedService] public class Thing : IThing;")
        );
        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var firstA = first.ServiceProvider.GetService(serviceType);
        var firstB = first.ServiceProvider.GetService(serviceType);
        var secondA = second.ServiceProvider.GetService(serviceType);

        Verify(ReferenceEquals(firstA, firstB));
        Verify(!ReferenceEquals(firstA, secondA));
    }

    [VerifyTest]
    [Row("SingletonService", ServiceLifetime.Singleton)]
    [Row("ScopedService", ServiceLifetime.Scoped)]
    [Row("TransientService", ServiceLifetime.Transient)]
    public void ServiceAttribute_RegistersTheMatchingLifetime(
        string attribute,
        ServiceLifetime expected
    )
    {
        var generated = GeneratedAssembly.Create(
            Module($"[{attribute}] public class Thing : IThing;")
        );

        Verify(generated.Descriptor("IThing").Lifetime == expected);
    }

    [VerifyTest]
    public void AsProperty_ResolvesUnderTheRequestedServiceTypeOnly()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public interface IOther;
                [SingletonService(As = typeof(IOther))] public class Thing : IThing, IOther;
                """
            )
        );

        var provider = generated.BuildProvider();

        Verify(provider.GetService(generated.Type("IOther")) is not null);
        Verify(provider.GetService(generated.Type("IThing")) is null);
    }

    [VerifyTest]
    public void KeyedService_ResolvesOnlyThroughItsKey()
    {
        var generated = GeneratedAssembly.Create(
            Module("""[SingletonService(Key = "the-key")] public class Thing : IThing;""")
        );

        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Verify(provider.GetKeyedService(serviceType, "the-key") is not null);
        Verify(provider.GetService(serviceType) is null);
    }

    [VerifyTest]
    public void KeyedServices_WithDifferentKeysResolveDifferentImplementations()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [SingletonService(Key = "first")] public class FirstThing : IThing;
                [SingletonService(Key = "second")] public class SecondThing : IThing;
                """
            )
        );

        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IThing");

        Verify(
            provider.GetKeyedService(serviceType, "first")!.GetType()
                == generated.Type("FirstThing")
        );
        Verify(
            provider.GetKeyedService(serviceType, "second")!.GetType()
                == generated.Type("SecondThing")
        );
    }

    /// <summary>
    /// The contract of [CrossWireService]: one instance, reachable through the implementation type
    /// and through every interface it implements.
    /// </summary>
    [VerifyTest]
    public void CrossWireService_SharesOneInstanceAcrossAllItsInterfaces()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public interface IOther;
                [CrossWireService(Lifetime = ServiceLifetime.Singleton)]
                public class Thing : IThing, IOther;
                """,
                extraUsings: "using Microsoft.Extensions.DependencyInjection;"
            )
        );

        var provider = generated.BuildProvider();

        var asThing = provider.GetService(generated.Type("IThing"));
        var asOther = provider.GetService(generated.Type("IOther"));
        var asImplementation = provider.GetService(generated.Type("Thing"));

        Verify(asThing is not null);
        Verify(ReferenceEquals(asThing, asOther));
        Verify(ReferenceEquals(asThing, asImplementation));
    }

    [VerifyTest]
    public void TryRegistration_DoesNotReplaceAnExistingRegistration()
    {
        var generated = GeneratedAssembly.Create(
            Module("[SingletonService(Using = RegistrationType.Try)] public class Thing : IThing;")
        );

        Verify(generated.Descriptor("IThing").Lifetime == ServiceLifetime.Singleton);
        Verify(generated.ResolveRequired("IThing").GetType() == generated.Type("Thing"));
    }

    [VerifyTest]
    public void ReplaceRegistration_LeavesASingleRegistration()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                "[SingletonService(Using = RegistrationType.Replace)] public class Thing : IThing;"
            )
        );

        Single(generated.Descriptors("IThing"));
    }

    /// <summary>
    /// Registrations within a module are emitted sorted by implementation type name, and both
    /// Replace and Try act on a registration that has to already be there. Named so that the
    /// alphabet puts them first, they used to run before their target existed: Replace replaced
    /// nothing and added itself, then the registration it meant to displace was added after it and
    /// won. Renaming the class fixed it, and nothing said so.
    /// </summary>
    [VerifyTest]
    public void ReplaceRegistration_WinsEvenWhenItsTypeNameSortsFirst()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [SingletonService(Using = RegistrationType.Replace)] public class AaaThing : IThing;
                [SingletonService] public class ZzzThing : IThing;
                """
            )
        );

        Single(generated.Descriptors("IThing"));
        Verify(generated.ResolveRequired("IThing").GetType() == generated.Type("AaaThing"));
    }

    [VerifyTest]
    public void TryRegistration_DeclinesEvenWhenItsTypeNameSortsFirst()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [SingletonService(Using = RegistrationType.Try)] public class AaaThing : IThing;
                [SingletonService] public class ZzzThing : IThing;
                """
            )
        );

        Single(generated.Descriptors("IThing"));
        Verify(generated.ResolveRequired("IThing").GetType() == generated.Type("ZzzThing"));
    }

    [VerifyTest]
    public void ConstructorDependencies_AreInjectedFromTheContainer()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public interface IDependency;

                [SingletonService] public class Dependency : IDependency;

                [SingletonService]
                public class Thing : IThing {
                    public IDependency Injected { get; }
                    public Thing(IDependency dependency) => Injected = dependency;
                }
                """
            )
        );

        var provider = generated.BuildProvider();
        var thing = provider.GetService(generated.Type("IThing"))!;

        var injected = thing.GetType().GetProperty("Injected")!.GetValue(thing);

        Verify(injected is not null);
        Verify(ReferenceEquals(provider.GetService(generated.Type("IDependency")), injected));
    }

    [VerifyTest]
    public void StaticFactory_IsInvokedToCreateTheService()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public class Thing : IThing {
                    public string Origin { get; private set; } = "constructor";

                    [SingletonService]
                    public static IThing Create() => new Thing { Origin = "factory" };
                }
                """
            )
        );

        var thing = generated.ResolveRequired("IThing");

        Verify(Equals(thing.GetType().GetProperty("Origin")!.GetValue(thing), "factory"));
    }

    [VerifyTest]
    public void StaticFactory_ReceivesItsDependenciesFromTheContainer()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public interface IDependency;

                [SingletonService] public class Dependency : IDependency;

                public class Thing : IThing {
                    public IDependency? Injected { get; private set; }

                    [SingletonService]
                    public static IThing Create(IDependency dependency) => new Thing { Injected = dependency };
                }
                """
            )
        );

        var provider = generated.BuildProvider();
        var thing = provider.GetService(generated.Type("IThing"))!;

        Verify(
            ReferenceEquals(
                provider.GetService(generated.Type("IDependency")),
                thing.GetType().GetProperty("Injected")!.GetValue(thing)
            )
        );
    }

    [VerifyTest]
    public void OpenGenericService_ResolvesForAnyTypeArgument()
    {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class GenericThing<T> : IGeneric<T>;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = generated.BuildProvider();
        var closed = generated.Type("IGeneric`1").MakeGenericType(typeof(string));

        Verify(provider.GetService(closed) is not null);
    }

    [VerifyTest]
    public void ClosedGenericService_ResolvesForItsOwnArgumentOnly()
    {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class StringGeneric : IGeneric<string>;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = generated.BuildProvider();
        var generic = generated.Type("IGeneric`1");

        Verify(provider.GetService(generic.MakeGenericType(typeof(string))) is not null);
        Verify(provider.GetService(generic.MakeGenericType(typeof(int))) is null);
    }

    [VerifyTest]
    public void NestedService_ResolvesThroughItsContainingType()
    {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                public static class Outer {
                    [SingletonService]
                    public class Inner : IThing;
                }
                """
            )
        );

        Verify(generated.ResolveRequired("IThing").GetType().Name == "Inner");
    }

    [VerifyTest]
    public void RecordService_Resolves()
    {
        var generated = GeneratedAssembly.Create(
            Module("[SingletonService] public record ThingRecord : IThing;")
        );

        Verify(generated.ResolveRequired("IThing").GetType() == generated.Type("ThingRecord"));
    }

    [VerifyTest]
    public void ModuleConfiguration_RunsAlongsideGeneratedRegistrations()
    {
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
            """
        );

        var provider = generated.BuildProvider();

        Verify(provider.GetService(generated.Type("IThing")) is not null);
        Verify(provider.GetService(generated.Type("IManual")) is not null);
    }

    [VerifyTest]
    public void OnlyRealm_RegistersOnlyServicesInThatRealm()
    {
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
            "RealmModule"
        );

        var provider = generated.BuildProvider();

        Verify(provider.GetService(generated.Type("IInRealm")) is not null);
        Verify(provider.GetService(generated.Type("IOutsideRealm")) is null);
    }

    [VerifyTest]
    public void ComposedModule_AppliesTheModulesItReferences()
    {
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
            "ComposedModule"
        );

        Verify(generated.BuildProvider().GetService(generated.Type("IFromBase")) is not null);
    }

    [VerifyTest]
    public void ServiceWithNoInterface_ResolvesAsItself()
    {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [SingletonService]
            public class Standalone;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(generated.BuildProvider().GetService(generated.Type("Standalone")) is not null);
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

using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Behavioural coverage of the registration code the generator emits for each service attribute.
/// These assert on the shape of the output rather than an exact snapshot, so they stay readable
/// when unrelated parts of the generated file change.
/// </summary>
public partial class ServiceRegistrationTests
{
    [VerifyTest]
    public void SingletonService_EmitsAddSingleton()
    {
        var result = GeneratorTestHarness.Run(
            Module("[SingletonService] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("AddSingleton"));
    }

    [VerifyTest]
    public void ScopedService_EmitsAddScoped()
    {
        var result = GeneratorTestHarness.Run(
            Module("[ScopedService] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("AddScoped"));
    }

    [VerifyTest]
    public void TransientService_EmitsAddTransient()
    {
        var result = GeneratorTestHarness.Run(
            Module("[TransientService] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("AddTransient"));
    }

    [VerifyTest]
    public void Service_RegistersImplementedInterfaceAsServiceType()
    {
        var result = GeneratorTestHarness.Run(
            Module("[SingletonService] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.IThing"));
        Verify(generated.Contains("global::TestNamespace.Thing"));
    }

    [VerifyTest]
    public void KeyedService_EmitsKeyedRegistration()
    {
        var result = GeneratorTestHarness.Run(
            Module("""[SingletonService(Key = "the-key")] public class Thing : IThing;""")
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("AddKeyedSingleton"));
        Verify(generated.Contains("\"the-key\""));
    }

    [VerifyTest]
    public void AsProperty_RegistersTheRequestedServiceType()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                public interface IOther;
                [SingletonService(As = typeof(IOther))] public class Thing : IThing, IOther;
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.IOther"));
    }

    [VerifyTest]
    [Row("RegistrationType.Try", "TryAddSingleton")]
    [Row("RegistrationType.TryEnumerable", "TryAddEnumerable")]
    [Row("RegistrationType.Replace", "Replace")]
    public void UsingProperty_ChangesTheRegistrationMethod(
        string registrationType,
        string expectedCall
    )
    {
        var result = GeneratorTestHarness.Run(
            Module($"[SingletonService(Using = {registrationType})] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains(expectedCall));
    }

    [VerifyTest]
    public void CrossWireService_RegistersImplementationAndInterface()
    {
        var result = GeneratorTestHarness.Run(
            Module("[CrossWireService] public class Thing : IThing;")
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.Thing"));
        Verify(generated.Contains("global::TestNamespace.IThing"));
    }

    /// <summary>
    /// Both spellings of the same attribute select the declaration.
    /// </summary>
    /// <remarks>
    /// A namespace-qualified usage is selected too, but the model builder still classifies
    /// attributes by their written name, so it produces no registrations from one. That is a
    /// separate limitation and not asserted here.
    /// </remarks>
    [VerifyTest]
    [Row("[SingletonService]")]
    [Row("[SingletonServiceAttribute]")]
    public void ServiceAttribute_IsMatchedHoweverItIsWritten(string attribute)
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            {{attribute}}
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(generated.ResolveRequired("IThing") is not null);
    }

    /// <summary>
    /// And an attribute that merely shares a name is not one of ours.
    /// </summary>
    [VerifyTest]
    public void SameNamedAttributeFromAnotherNamespace_IsIgnored()
    {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace Other {
                public class SingletonServiceAttribute : System.Attribute;
            }

            namespace TestNamespace {
                public interface IThing;

                [Other.SingletonService]
                public class Thing : IThing;

                [DependencyModule]
                public partial class TestModule;
            }
            """
        );

        Verify(generated.Descriptors("IThing").Count == 0);
    }

    [VerifyTest]
    public void OpenGenericService_RegistersOpenGenericTypes()
    {
        var result = GeneratorTestHarness.Run(
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

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("IGeneric<>"));
        Verify(generated.Contains("GenericThing<>"));
    }

    [VerifyTest]
    public void StaticFactoryMethod_IsRegisteredAsAFactory()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            public class Thing : IThing {
                [SingletonService]
                public static IThing Create() => new Thing();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("Create"));
        Verify(generated.Contains("AddSingleton"));
    }

    /// <summary>
    /// A factory method registers its declared return type. The interfaces of that type are not
    /// inferred.
    /// </summary>
    [VerifyTest]
    public void FactoryMethod_RegistersItsDeclaredReturnType()
    {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            public class Thing : IThing;

            public static class ThingFactory {
                [SingletonService]
                public static Thing Create() => new Thing();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Single(generated.Descriptors("Thing"));
        Verify(generated.Descriptors("IThing").Count == 0);
    }

    [VerifyTest]
    public void ModuleWithNoServices_DoesNotEmitADependenciesFile()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        Verify(!result.GeneratedSources.Keys.Any(key => key.Contains("Dependencies")));
    }

    [VerifyTest]
    public void RecordModule_IsSupported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial record TestModule;
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining("Dependencies").Contains("AddSingleton"));
    }

    /// <summary>
    /// The first interface in the declaration used to win outright, so a class that cleaned up after
    /// itself registered as IDisposable and was unreachable through the interface it existed for.
    /// </summary>
    [VerifyTest]
    public void CapabilityInterface_DoesNotWinOverTheServiceInterface()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                [SingletonService]
                public class Thing : System.IDisposable, IThing {
                    public void Dispose() { }
                }
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.IThing"));
        Verify(!generated.Contains("System.IDisposable"));
    }

    [VerifyTest]
    public void CapabilityInterfaceAlone_RegistersAsSelf()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                [SingletonService]
                public class Thing : System.IDisposable {
                    public void Dispose() { }
                }
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.Thing"));
        Verify(!generated.Contains("System.IDisposable"));
    }

    [VerifyTest]
    public void CapabilityInterfaceThroughABaseClass_RegistersAsSelf()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                public abstract class DisposableBase : System.IDisposable {
                    public void Dispose() { }
                }

                [SingletonService]
                public class Thing : DisposableBase;
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("global::TestNamespace.Thing"));
        Verify(!generated.Contains("System.IDisposable"));
    }

    /// <summary>
    /// Guards the boundary of the capability list: <c>System</c> is full of interfaces that are
    /// genuine service roles, so this must not become a namespace rule. IJsonTypeInfoResolver and
    /// IHttpClientFactory are the same shape.
    /// </summary>
    [VerifyTest]
    public void FrameworkRoleInterface_IsStillTheServiceType()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                [SingletonService]
                public class Thing : System.Collections.Generic.IEqualityComparer<IThing> {
                    public bool Equals(IThing? a, IThing? b) => false;
                    public int GetHashCode(IThing o) => 0;
                }
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("IEqualityComparer"));
    }

    [VerifyTest]
    public void CapabilityInterface_IsHonouredWhenNamedExplicitly()
    {
        var result = GeneratorTestHarness.Run(
            Module(
                """
                [SingletonService(As = typeof(System.IDisposable))]
                public class Thing : System.IDisposable, IThing {
                    public void Dispose() { }
                }
                """
            )
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Verify(generated.Contains("System.IDisposable"));
    }

    private static string Module(string body) =>
        $$"""
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            {{body}}

            [DependencyModule]
            public partial class TestModule;
            """;
}

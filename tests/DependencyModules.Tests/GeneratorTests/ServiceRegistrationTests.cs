using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Behavioural coverage of the registration code the generator emits for each service attribute.
/// These assert on the shape of the output rather than an exact snapshot, so they stay readable
/// when unrelated parts of the generated file change.
/// </summary>
public class ServiceRegistrationTests {

    [Fact]
    public void SingletonService_EmitsAddSingleton() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Contains("AddSingleton", result.SourceContaining("Dependencies"));
    }

    [Fact]
    public void ScopedService_EmitsAddScoped() {
        var result = GeneratorTestHarness.Run(Module("[ScopedService] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Contains("AddScoped", result.SourceContaining("Dependencies"));
    }

    [Fact]
    public void TransientService_EmitsAddTransient() {
        var result = GeneratorTestHarness.Run(Module("[TransientService] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Contains("AddTransient", result.SourceContaining("Dependencies"));
    }

    [Fact]
    public void Service_RegistersImplementedInterfaceAsServiceType() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public class Thing : IThing;"));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.IThing", generated);
        Assert.Contains("global::TestNamespace.Thing", generated);
    }

    [Fact]
    public void KeyedService_EmitsKeyedRegistration() {
        var result = GeneratorTestHarness.Run(
            Module("""[SingletonService(Key = "the-key")] public class Thing : IThing;"""));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("AddKeyedSingleton", generated);
        Assert.Contains("\"the-key\"", generated);
    }

    [Fact]
    public void AsProperty_RegistersTheRequestedServiceType() {
        var result = GeneratorTestHarness.Run(Module(
            """
            public interface IOther;
            [SingletonService(As = typeof(IOther))] public class Thing : IThing, IOther;
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.IOther", generated);
    }

    [Theory]
    [InlineData("RegistrationType.Try", "TryAddSingleton")]
    [InlineData("RegistrationType.TryEnumerable", "TryAddEnumerable")]
    [InlineData("RegistrationType.Replace", "Replace")]
    public void UsingProperty_ChangesTheRegistrationMethod(string registrationType, string expectedCall) {
        var result = GeneratorTestHarness.Run(
            Module($"[SingletonService(Using = {registrationType})] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Contains(expectedCall, result.SourceContaining("Dependencies"));
    }

    [Fact]
    public void CrossWireService_RegistersImplementationAndInterface() {
        var result = GeneratorTestHarness.Run(Module("[CrossWireService] public class Thing : IThing;"));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.Thing", generated);
        Assert.Contains("global::TestNamespace.IThing", generated);
    }

    /// <summary>
    /// Both spellings of the same attribute select the declaration.
    /// </summary>
    /// <remarks>
    /// A namespace-qualified usage is selected too, but the model builder still classifies
    /// attributes by their written name, so it produces no registrations from one. That is a
    /// separate limitation and not asserted here.
    /// </remarks>
    [Theory]
    [InlineData("[SingletonService]")]
    [InlineData("[SingletonServiceAttribute]")]
    public void ServiceAttribute_IsMatchedHoweverItIsWritten(string attribute) {
        var generated = GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;

              namespace TestNamespace;

              public interface IThing;

              {{attribute}}
              public class Thing : IThing;

              [DependencyModule]
              public partial class TestModule;
              """);

        Assert.NotNull(generated.ResolveRequired("IThing"));
    }

    /// <summary>
    /// And an attribute that merely shares a name is not one of ours.
    /// </summary>
    [Fact]
    public void SameNamedAttributeFromAnotherNamespace_IsIgnored() {
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
            """);

        Assert.Empty(generated.Descriptors("IThing"));
    }

    [Fact]
    public void OpenGenericService_RegistersOpenGenericTypes() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class GenericThing<T> : IGeneric<T>;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("IGeneric<>", generated);
        Assert.Contains("GenericThing<>", generated);
    }

    [Fact]
    public void StaticFactoryMethod_IsRegisteredAsAFactory() {
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
            """);

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("Create", generated);
        Assert.Contains("AddSingleton", generated);
    }

    [Fact]
    public void ModuleWithNoServices_DoesNotEmitADependenciesFile() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Dependencies"));
    }

    [Fact]
    public void RecordModule_IsSupported() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial record TestModule;
            """);

        result.AssertNoErrors();
        Assert.Contains("AddSingleton", result.SourceContaining("Dependencies"));
    }

    /// <summary>
    /// The first interface in the declaration used to win outright, so a class that cleaned up after
    /// itself registered as IDisposable and was unreachable through the interface it existed for.
    /// </summary>
    [Fact]
    public void CapabilityInterface_DoesNotWinOverTheServiceInterface() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [SingletonService]
            public class Thing : System.IDisposable, IThing {
                public void Dispose() { }
            }
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.IThing", generated);
        Assert.DoesNotContain("System.IDisposable", generated);
    }

    [Fact]
    public void CapabilityInterfaceAlone_RegistersAsSelf() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [SingletonService]
            public class Thing : System.IDisposable {
                public void Dispose() { }
            }
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.Thing", generated);
        Assert.DoesNotContain("System.IDisposable", generated);
    }

    [Fact]
    public void CapabilityInterfaceThroughABaseClass_RegistersAsSelf() {
        var result = GeneratorTestHarness.Run(Module(
            """
            public abstract class DisposableBase : System.IDisposable {
                public void Dispose() { }
            }

            [SingletonService]
            public class Thing : DisposableBase;
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("global::TestNamespace.Thing", generated);
        Assert.DoesNotContain("System.IDisposable", generated);
    }

    /// <summary>
    /// Guards the boundary of the capability list: <c>System</c> is full of interfaces that are
    /// genuine service roles, so this must not become a namespace rule. IJsonTypeInfoResolver and
    /// IHttpClientFactory are the same shape.
    /// </summary>
    [Fact]
    public void FrameworkRoleInterface_IsStillTheServiceType() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [SingletonService]
            public class Thing : System.Collections.Generic.IEqualityComparer<IThing> {
                public bool Equals(IThing? a, IThing? b) => false;
                public int GetHashCode(IThing o) => 0;
            }
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("IEqualityComparer", generated);
    }

    [Fact]
    public void CapabilityInterface_IsHonouredWhenNamedExplicitly() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [SingletonService(As = typeof(System.IDisposable))]
            public class Thing : System.IDisposable, IThing {
                public void Dispose() { }
            }
            """));

        result.AssertNoErrors();
        var generated = result.SourceContaining("Dependencies");

        Assert.Contains("System.IDisposable", generated);
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

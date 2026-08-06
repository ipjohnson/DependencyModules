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

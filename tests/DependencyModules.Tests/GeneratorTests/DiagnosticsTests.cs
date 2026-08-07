using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator's job is to move failures from run time to build time. Each of these covers a
/// mistake that previously produced either a crash when the container was built or, worse, a
/// successful build that quietly registered nothing.
/// </summary>
public class DiagnosticsTests {

    /// <summary>
    /// An abstract implementation used to be registered anyway, and the resulting
    /// AddSingleton(typeof(IThing), typeof(AbstractThing)) threw when the provider was built,
    /// a long way from the declaration responsible.
    /// </summary>
    [Fact]
    public void AbstractService_ReportsDM0002() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public abstract class Thing : IThing;"));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0002");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Thing", diagnostic.GetMessage());
        Assert.Contains("abstract", diagnostic.GetMessage());
    }

    [Fact]
    public void AbstractService_IsNotRegistered() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public abstract class Thing : IThing;"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Dependencies"));
    }

    [Fact]
    public void StaticService_ReportsDM0002() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [SingletonService]
            public static class StaticThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0002");

        Assert.Contains("static", diagnostic.GetMessage());
    }

    /// <summary>
    /// A concrete service alongside an unconstructable one must still be registered; reporting the
    /// bad one should not discard the good ones.
    /// </summary>
    [Fact]
    public void ConcreteServices_AreStillRegisteredAlongsideARejectedOne() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;
            public interface IAbstract;

            [SingletonService] public class Thing : IThing;

            [SingletonService] public abstract class AbstractThing : IAbstract;

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();

        Assert.NotNull(provider.GetService(generated.Type("IThing")));
        Assert.Null(provider.GetService(generated.Type("IAbstract")));
    }

    /// <summary>
    /// The compiler reports CS0260 once the generated half arrives, but that describes the symptom.
    /// DM0003 names the fix, and generation is skipped so it is the only error shown.
    /// </summary>
    [Fact]
    public void NonPartialModule_ReportsDM0003() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService] public class Thing : IThing;

            [DependencyModule]
            public class NotPartialModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0003");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("NotPartialModule", diagnostic.GetMessage());
        Assert.Contains("partial", diagnostic.GetMessage());
    }

    [Fact]
    public void NonPartialModule_DoesNotGenerateAConflictingDeclaration() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public class NotPartialModule;
            """);

        // Emitting the module half would add CS0260 on top of DM0003 and point at the wrong thing.
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("NotPartialModule.Module"));
        Assert.DoesNotContain(result.CompilationDiagnostics, d => d.Id == "CS0260");
    }

    [Fact]
    public void PartialModule_ReportsNothing() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Empty(result.GeneratorDiagnostics);
    }

    [Fact]
    public void AbstractFactoryHost_IsAllowedBecauseTheFactorySuppliesTheInstance() {
        // The declaring type is never constructed, so an abstract host is legitimate here.
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            public abstract class Factories {
                [SingletonService]
                public static IThing Create() => null!;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0002");
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

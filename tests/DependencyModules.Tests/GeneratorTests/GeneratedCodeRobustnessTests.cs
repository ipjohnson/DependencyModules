using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Generated code lands in someone else's project, so it must not depend on how that project is
/// configured. The harness compiles without implicit usings and without any project-level using
/// directives, which is the strictest environment a consumer can present.
/// </summary>
public class GeneratedCodeRobustnessTests {

    /// <summary>
    /// Regression test: the generated module attribute used to be emitted as a bare
    /// <c>[AttributeUsage(AttributeTargets...)]</c>, which only compiled because the consuming
    /// project happened to have ImplicitUsings enabled. Projects with ImplicitUsings disabled
    /// failed with CS0246/CS0103 on every generated module.
    /// </summary>
    [Fact]
    public void GeneratedCode_CompilesWithoutImplicitUsings() {
        var result = GeneratorTestHarness.Run(
            """
            namespace TestNamespace;

            public interface IThing;

            [DependencyModules.Runtime.Attributes.SingletonService]
            public class Thing : IThing;

            [DependencyModules.Runtime.Attributes.DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
    }

    [Fact]
    public void GeneratedModuleAttribute_UsesFullyQualifiedAttributeUsage() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Assert.Contains("global::System.AttributeUsage", generated);
        Assert.DoesNotContain("[AttributeUsage(", generated);
    }

    /// <summary>
    /// A consumer that treats warnings as errors should not be broken by generated code.
    /// </summary>
    [Fact]
    public void GeneratedCode_ProducesNoWarnings() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();

        var generatedTreePaths = result.Compilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(tree => tree.FilePath)
            .ToHashSet(StringComparer.Ordinal);

        var warnings = result.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Where(diagnostic => generatedTreePaths.Contains(diagnostic.Location.SourceTree?.FilePath ?? ""))
            .ToArray();

        Assert.True(warnings.Length == 0,
            "Generated code produced warnings:" + Environment.NewLine +
            string.Join(Environment.NewLine, warnings.Select(w => $"  {w.Id} {w.GetMessage()}")));
    }

    /// <summary>
    /// Regression test: a registered service type keeps whatever nullable annotation its declaration
    /// used, so <c>IHandler&lt;Query, Result?&gt;</c> emits <c>typeof(…Result?)</c>. Roslyn requires
    /// generated code to open a nullable context explicitly however the consuming project is
    /// configured, and the registrations file was the one generated file that never did — making a
    /// find-by-id handler, the most ordinary shape there is, CS8669 and a build break under
    /// TreatWarningsAsErrors that the consumer could not fix from their own source.
    ///
    /// GeneratedCode_ProducesNoWarnings above covers the same ground but registers a service whose
    /// type arguments carry no annotation, which is why it stayed green throughout.
    /// </summary>
    [Theory]
    [InlineData("attribute", "[SingletonService]")]
    [InlineData("convention", "")]
    public void GeneratedCode_ProducesNoWarnings_ForANullableTypeArgument(string _, string attribute) {
        var result = GeneratorTestHarness.Run(
            $$"""
              using DependencyModules.Runtime.Attributes;
              using DependencyModules.Runtime.Conventions;

              namespace TestNamespace;

              public class Book;

              public interface IHandler<TQuery, TResult> {
                  TResult Handle(TQuery query);
              }

              public record GetBook(string Isbn);

              {{attribute}}
              public class GetBookHandler : IHandler<GetBook, Book?> {
                  public Book? Handle(GetBook query) => null;
              }

              [DependencyModule]
              public partial class TestModule : IConventionModule {
                  void IConventionModule.Conventions(IConventionDefinitions conventions) {
                      conventions.RegisterAll(typeof(IHandler<,>)).AsScoped();
                  }
              }
              """);

        result.AssertNoErrors();

        AssertNoWarningsFromGeneratedCode(result);
    }

    private static void AssertNoWarningsFromGeneratedCode(GeneratorResult result) {
        var generatedTreePaths = result.Compilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(tree => tree.FilePath)
            .ToHashSet(StringComparer.Ordinal);

        var warnings = result.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Where(diagnostic => generatedTreePaths.Contains(diagnostic.Location.SourceTree?.FilePath ?? ""))
            .ToArray();

        Assert.True(warnings.Length == 0,
            "Generated code produced warnings:" + Environment.NewLine +
            string.Join(Environment.NewLine, warnings.Select(w => $"  {w.Id} {w.GetMessage()}")));
    }

    [Fact]
    public void GeneratedCode_QualifiesReferencesToTheRuntime() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();

        Assert.Contains(
            "global::DependencyModules.Runtime.Helpers.DependencyRegistry",
            result.SourceContaining(".Module.g.cs"));
    }

    [Fact]
    public void GeneratorRun_ReportsNoDiagnosticsForValidInput() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.GeneratorDiagnostics);
    }
}

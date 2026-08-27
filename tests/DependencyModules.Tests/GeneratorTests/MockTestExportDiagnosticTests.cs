using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// DM0021: a <c>[Mock]</c> parameter and a <c>[TestExport]</c> on the same method, both naming one
/// service.
///
/// A parameter attribute is the narrowest thing a test can say, so <c>[Mock]</c> wins and the
/// <c>[TestExport]</c> beside it does nothing. Written on the same method the two say opposite
/// things about one service in the same breath, and only one of them can have been meant — so it is
/// reported rather than resolved quietly, which is how the field report met it.
///
/// The scope is the whole point. A <c>[TestExport]</c> on the class or the assembly is a default for
/// everything under it, and one test overriding it for one argument is exactly what having both
/// scopes is for. Reporting that would be reporting the feature.
/// </summary>
public class MockTestExportDiagnosticTests {

    [Fact]
    public void BothOnOneMethod_ReportsDM0021() {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([Mock] IThing thing) { }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");

        Assert.Contains("Conflicting", diagnostic.GetMessage());
        Assert.Contains("IThing", diagnostic.GetMessage());
    }

    /// <summary>
    /// Reported at the parameter, which is the half that wins and the half a reader has to look at
    /// to see what actually happens.
    /// </summary>
    [Fact]
    public void ItIsReportedAtTheParameter() {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([Mock] IThing thing) { }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");

        Assert.NotNull(diagnostic.Location.SourceTree);
        Assert.Equal("Test.cs", System.IO.Path.GetFileName(diagnostic.Location.GetLineSpan().Path));
    }

    /// <summary>
    /// The class-level default a test opts out of. This is the shape the override exists for.
    /// </summary>
    [Fact]
    public void TestExportOnTheClass_IsNotReported() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
              public class Fixture {
                  public void Overriding([Mock] IThing thing) { }
              }
              """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    [Fact]
    public void TestExportOnTheAssembly_IsNotReported() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              [assembly: TestExport(typeof(IThing), Implementation = typeof(RealThing))]

              public class Fixture {
                  public void Overriding([Mock] IThing thing) { }
              }
              """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    /// <summary>
    /// Naming different services is two unrelated declarations, not a disagreement.
    /// </summary>
    [Fact]
    public void BothOnOneMethodNamingDifferentServices_IsNotReported() {
        var result = Run(
            """
            [TestExport(typeof(IOther), Implementation = typeof(RealOther))]
            public void Unrelated([Mock] IThing thing) { }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    [Fact]
    public void AMockWithNoTestExport_IsNotReported() {
        var result = Run("public void JustAMock([Mock] IThing thing) { }");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    [Fact]
    public void ATestExportWithNoMock_IsNotReported() {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void JustAnExport(IThing thing) { }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    /// <summary>
    /// Resolved rather than string-matched, the way every other attribute this generator reads is.
    /// </summary>
    [Fact]
    public void QualifiedSpellings_AreStillReported() {
        var result = Run(
            """
            [DependencyModules.Testing.Attributes.TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([global::DependencyModules.Testing.Attributes.Mock] IThing thing) { }
            """);

        Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    private const string Preamble =
        """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Testing.Attributes;

        namespace TestNamespace;

        public interface IThing;

        public interface IOther;

        public class RealThing : IThing;

        public class RealOther : IOther;
        """;

    private static GeneratorResult Run(string body) =>
        GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              public class Fixture {
                  {{body}}
              }
              """);
}

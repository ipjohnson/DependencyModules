using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Declaring <c>[DependencyModule] partial class ApplicationModule</c> in a project that already
/// gets one generated for it.
///
/// Two conditions have to hold together: top-level statements, which is what makes the generator
/// emit its own <c>ApplicationModule</c> into the project's RootNamespace, and a declared module of
/// that name in that same namespace. Neither alone does anything. Getting Started tells the reader
/// to use exactly that name, and putting your own classes in the project's root namespace is the
/// default, so the pair is not an unusual thing to write.
///
/// What it produced was CS8785 — a *warning*, so invisible at <c>-v quiet</c> or under NoWarn —
/// followed by CS0311 blaming the reader's own type for not implementing an interface they had
/// never heard of. The two models had different namespaces at the point they were grouped (the
/// generated one is created with none and given the RootNamespace afterwards), so they were never
/// recognised as the same module; by the time the file name was computed they had converged, and
/// the RootNamespace prefix is stripped out of it, so both asked for ApplicationModule.Module.g.cs.
///
/// The variants below are kept as a set on purpose: every one of them was reported as the trigger
/// at some point, and only the last is.
/// </summary>
public class ApplicationModuleCollisionTests {

    [Fact]
    public void DeclaringApplicationModuleInTheRootNamespace_DoesNotCollide() {
        var result = Run(DeclaredModule("ConfiguredRoot"));

        Assert.Empty(result.DuplicateHintNames);
        result.AssertNoErrors();
    }

    /// <summary>
    /// CS8785 is how a duplicate hint name reaches the build, and it is only a warning — which is
    /// the reason this shipped. Asserted separately from AssertNoErrors for that reason.
    /// </summary>
    [Fact]
    public void DeclaringApplicationModuleInTheRootNamespace_DoesNotWarn() {
        var result = Run(DeclaredModule("ConfiguredRoot"));

        Assert.DoesNotContain(
            result.CompilationDiagnostics.Concat(result.GeneratorDiagnostics),
            diagnostic => diagnostic.Id == "CS8785");
    }

    /// <summary>
    /// The declared module is the one that survives: it is the one with a body the developer can
    /// add to. Consolidation already preferred a declared module over a generated one wherever it
    /// recognised the two as the same — this only ever failed to recognise them.
    /// </summary>
    [Fact]
    public void TheDeclaredModuleIsTheOneGenerated() {
        var result = Run(DeclaredModule("ConfiguredRoot"));

        var module = result.SourceContaining("ApplicationModule.Module");

        Assert.Contains("namespace ConfiguredRoot", module);
    }

    /// <summary>
    /// The registrations still have to arrive. Merging the two models is only correct if the
    /// surviving one carries what the project asked for.
    /// </summary>
    [Fact]
    public void TheSurvivingModuleStillRegistersTheProjectsServices() {
        var result = Run(DeclaredModule("ConfiguredRoot"));

        Assert.Contains("Thing", result.SourceContaining("ApplicationModule.Dependencies"));
    }

    /// <summary>
    /// The four shapes that always built cleanly, kept so that a fix cannot quietly change them.
    /// Each differs from the failing case in exactly one way.
    /// </summary>
    [Theory]
    // A different name never collides with the generated ApplicationModule.
    [InlineData("different name", """
                                  namespace ConfiguredRoot;
                                  [DependencyModules.Runtime.Attributes.DependencyModule]
                                  public partial class CompositionModule;
                                  """)]
    // A namespace other than RootNamespace produces a different hint name.
    [InlineData("different namespace", """
                                       namespace SomewhereElse;
                                       [DependencyModules.Runtime.Attributes.DependencyModule]
                                       public partial class ApplicationModule;
                                       """)]
    // The global namespace, likewise.
    [InlineData("global namespace", """
                                    [DependencyModules.Runtime.Attributes.DependencyModule]
                                    public partial class ApplicationModule;
                                    """)]
    public void AShapeThatNeverCollided_StillBuildsCleanly(string variant, string declaration) {
        var result = Run(declaration);

        Assert.Empty(result.DuplicateHintNames);
        result.AssertNoErrors();
        Assert.NotEmpty(variant);
    }

    /// <summary>
    /// An explicit Main was reported as one of the shapes that built cleanly. Measured here, it
    /// collides exactly like top-level statements do: the compilation unit is approved on its file
    /// name alone — <c>Program.cs</c> — with nothing checking for top-level statements, so a project
    /// with a hand-written Main in Program.cs gets a generated ApplicationModule too.
    ///
    /// That is worth knowing on its own, because it is not what the documentation describes. It is
    /// left as it is rather than narrowed: a project relying on the generated module today would
    /// lose it, and this fix makes the shape work either way.
    /// </summary>
    [Fact]
    public void AnExplicitMainWithADeclaredApplicationModule_BuildsCleanly() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Program.cs"] =
                    """
                    namespace ConfiguredRoot;

                    public static class Program {
                        public static void Main() => System.Console.WriteLine("hello");
                    }
                    """,
                ["Composition.cs"] = DeclaredModule("ConfiguredRoot"),
                ["Services.cs"] = Services
            },
            new Dictionary<string, string> { ["RootNamespace"] = "ConfiguredRoot" },
            OutputKind.ConsoleApplication);

        Assert.Empty(result.DuplicateHintNames);
        result.AssertNoErrors();
    }

    private static string DeclaredModule(string namespaceName) =>
        $$"""
          namespace {{namespaceName}};

          [DependencyModules.Runtime.Attributes.DependencyModule]
          public partial class ApplicationModule;
          """;

    private const string Services =
        """
        namespace ConfiguredRoot;

        public interface IThing;

        [DependencyModules.Runtime.Attributes.SingletonService]
        public class Thing : IThing;
        """;

    private static GeneratorResult Run(string declaration) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                // Top-level statements: this is what makes the generator emit its own
                // ApplicationModule into RootNamespace.
                ["Program.cs"] = """System.Console.WriteLine("hello");""",
                ["Composition.cs"] = declaration,
                ["Services.cs"] = Services
            },
            new Dictionary<string, string> { ["RootNamespace"] = "ConfiguredRoot" },
            OutputKind.ConsoleApplication);
}

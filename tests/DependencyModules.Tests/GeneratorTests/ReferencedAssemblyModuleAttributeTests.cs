using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// DM0016 and DM0019 for a module that lives in a referenced assembly.
///
/// This is the case both codes exist for. A module declared in the same project is the one shape a
/// developer can see all of; a module arriving from a package is the one where an assembly-level
/// attribute silently does nothing, because it was read by nobody and the failure is an
/// InvalidOperationException at the first resolve. The reference page prints exactly that shape as
/// its DM0019 example — <c>using MyApp.Library;</c> above <c>[assembly: LibraryModule]</c> — and it
/// did not fire, because both codes were built only from the modules declared in this compilation.
/// </summary>
public class ReferencedAssemblyModuleAttributeTests {

    private const string LibrarySource =
        """
        using DependencyModules.Runtime.Attributes;

        namespace ThePackage.Composition;

        public interface IPackageThing;

        [SingletonService]
        public class PackageThing : IPackageThing;

        [DependencyModule]
        public partial class LibraryModule;
        """;

    /// <summary>
    /// The documented DM0019 example, verbatim in shape: the attribute compiles, and it sits in a
    /// file the generated ApplicationModule was not built from, so nothing reads it.
    /// </summary>
    [Fact]
    public void AnAssemblyAttributeOutsideTheEntryPointFile_ReportsDM0019() {
        var result = Run(
            bootstrap: """
                       using ThePackage.Composition;

                       [assembly: LibraryModule]
                       """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0019");

        Assert.Contains("LibraryModule", diagnostic.GetMessage());
        Assert.Contains("Program.cs", diagnostic.GetMessage());
    }

    [Fact]
    public void AnAssemblyAttributeInTheEntryPointFile_IsSilent() {
        var result = Run(
            bootstrap: null,
            programExtra: """
                          using ThePackage.Composition;

                          [assembly: LibraryModule]
                          """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0019");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0016");
    }

    /// <summary>
    /// Without the using the attribute does not resolve at all, and the compiler's own message
    /// names a type the developer never wrote in a namespace it does not mention. DM0016 is what
    /// turns that into the one-line fix.
    /// </summary>
    [Fact]
    public void AnAssemblyAttributeWithoutItsNamespaceImported_ReportsDM0016() {
        var result = Run(bootstrap: "[assembly: LibraryModule]");

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0016");

        Assert.Contains("LibraryModule", diagnostic.GetMessage());
        Assert.Contains("ThePackage.Composition", diagnostic.GetMessage());
    }

    /// <summary>
    /// It does not compile yet, so which file it belongs in is a later question — the same order
    /// the in-compilation path already reports in.
    /// </summary>
    [Fact]
    public void AnAssemblyAttributeWithoutItsNamespaceImported_DoesNotAlsoReportDM0019() {
        var result = Run(bootstrap: "[assembly: LibraryModule]");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0019");
    }

    /// <summary>
    /// A global using satisfies the import wherever it is written, exactly as for a local module.
    /// </summary>
    [Fact]
    public void AGlobalUsingSatisfiesTheImport() {
        var result = Run(
            bootstrap: "[assembly: LibraryModule]",
            extraFiles: new Dictionary<string, string> {
                ["Usings.cs"] = "global using ThePackage.Composition;"
            });

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0016");
        Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0019");
    }

    /// <summary>
    /// A class library has no entry point, so nothing composes assembly-level attributes and there
    /// is nothing to be outside of. Unchanged by any of this, and worth pinning: a test project is
    /// the same shape, and that is where these attributes are supposed to live in their own file.
    /// </summary>
    [Fact]
    public void AClassLibrary_IsSilent() {
        var library = GeneratorTestHarness.CompileLibrary(LibrarySource, "TheModulePackage", runGenerator: true);

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Bootstrap.cs"] = """
                                   using ThePackage.Composition;

                                   [assembly: LibraryModule]
                                   """
            },
            additionalReferences: [library.Reference]);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0019");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0016");
    }

    /// <summary>
    /// An attribute naming no module anywhere is somebody else's attribute. Staying quiet for it is
    /// what keeps this from reporting on every unrelated assembly-level attribute in the project.
    /// </summary>
    [Fact]
    public void AnAttributeThatNamesNoModule_IsSilent() {
        var result = Run(bootstrap: "[assembly: System.CLSCompliant(true)]");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0016");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0019");
    }

    private static GeneratorResult Run(
        string? bootstrap,
        string? programExtra = null,
        IReadOnlyDictionary<string, string>? extraFiles = null) {

        var library = GeneratorTestHarness.CompileLibrary(LibrarySource, "TheModulePackage", runGenerator: true);

        var sources = new Dictionary<string, string> {
            // Top-level statements, so an ApplicationModule is generated and there is an entry
            // point file for DM0019 to measure against.
            ["Program.cs"] = (programExtra ?? "") + "\nSystem.Console.WriteLine(\"hello\");"
        };

        if (bootstrap != null) {
            sources["Bootstrap.cs"] = bootstrap;
        }

        foreach (var extra in extraFiles ?? new Dictionary<string, string>()) {
            sources[extra.Key] = extra.Value;
        }

        return GeneratorTestHarness.Run(
            sources,
            outputKind: OutputKind.ConsoleApplication,
            additionalReferences: [library.Reference]);
    }
}

using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// DM0016. A module generates its attribute in the module's own namespace, and an assembly-level
/// attribute has no namespace context to inherit, so <c>[assembly: ApplicationModule]</c> without a
/// <c>using</c> fails with CS0246 naming a type the developer never wrote.
///
/// The check is syntactic and cannot be otherwise: the attribute is written by the generator that is
/// running, so it does not exist in the compilation being examined and nothing about it resolves.
/// That makes the false positives the interesting cases, and most of these tests are one.
/// </summary>
public partial class AssemblyModuleAttributeDiagnosticsTests
{
    private const string ModuleInNamespace = """
        namespace MyApp.Composition;

        [DependencyModules.Runtime.Attributes.DependencyModule]
        public partial class ApplicationModule;
        """;

    [VerifyTest]
    public void MissingUsing_IsReported()
    {
        var result = Run("[assembly: ApplicationModule]");

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0016");

        Verify(diagnostic.GetMessage().Contains("MyApp.Composition"));
        Verify(diagnostic.GetMessage().Contains("using MyApp.Composition;"));
    }

    /// <summary>The suffixed spelling names the same module.</summary>
    [VerifyTest]
    public void MissingUsing_IsReported_ForTheAttributeSuffixedSpelling()
    {
        var result = Run("[assembly: ApplicationModuleAttribute]");

        Single(result.GeneratorDiagnostics, d => d.Id == "DM0016");
    }

    [VerifyTest]
    public void TheUsingBeingPresent_IsSilent()
    {
        var result = Run(
            """
            using MyApp.Composition;

            [assembly: ApplicationModule]
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    /// <summary>
    /// A global using in any file supplies the namespace everywhere, so reading only the file the
    /// attribute sits in would report a build that is already correct.
    /// </summary>
    [VerifyTest]
    public void AGlobalUsingInAnotherFile_IsSilent()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Module.cs"] = ModuleInNamespace,
                ["GlobalUsings.cs"] = "global using MyApp.Composition;",
                ["Bootstrap.cs"] = "[assembly: ApplicationModule]",
            }
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    [VerifyTest]
    public void AQualifiedUsage_IsSilent()
    {
        var result = Run("[assembly: MyApp.Composition.ApplicationModule]");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    /// <summary>An attribute this compilation declares no module for belongs to somebody else.</summary>
    [VerifyTest]
    public void AnUnrelatedAssemblyAttribute_IsSilent()
    {
        var result = Run("[assembly: System.Reflection.AssemblyMetadata(\"key\", \"value\")]");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    /// <summary>A module in the global namespace has no namespace to import.</summary>
    [VerifyTest]
    public void AModuleInTheGlobalNamespace_IsSilent()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Module.cs"] = """
                [DependencyModules.Runtime.Attributes.DependencyModule]
                public partial class ApplicationModule;
                """,
                ["Bootstrap.cs"] = "[assembly: ApplicationModule]",
            }
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    /// <summary>
    /// A using alias imports one name rather than a namespace, so it does not bring the attribute
    /// into scope under the name written here and the report still stands.
    /// </summary>
    [VerifyTest]
    public void AUsingAlias_DoesNotCountAsTheImport()
    {
        var result = Run(
            """
            using Composition = MyApp.Composition;

            [assembly: ApplicationModule]
            """
        );

        Single(result.GeneratorDiagnostics, d => d.Id == "DM0016");
    }

    /// <summary>
    /// DM0019. Assembly-level module attributes are composed into the generated
    /// <c>ApplicationModule</c>, which is built from one compilation unit. Written anywhere else
    /// they were read by nobody: a clean build, and an <c>InvalidOperationException</c> at the first
    /// resolve.
    /// </summary>
    [VerifyTest]
    public void AnAssemblyAttributeOutsideTheEntryPointFile_IsReported()
    {
        var result = RunWithEntryPoint(
            bootstrap: """
            using MyApp.Composition;

            [assembly: ApplicationModule]
            """,
            program: "System.Console.WriteLine();"
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0019");

        Verify(diagnostic.GetMessage().Contains("ApplicationModule"));
        Verify(diagnostic.GetMessage().Contains("Program.cs"));
    }

    /// <summary>
    /// A qualified name needs no import, so DM0016 cannot apply. The attribute is still read by
    /// nobody outside the entry point file.
    /// </summary>
    [VerifyTest]
    [Row("[assembly: MyApp.Composition.ApplicationModule]")]
    [Row("[assembly: MyApp.Composition.ApplicationModuleAttribute]")]
    [Row("[assembly: global::MyApp.Composition.ApplicationModule]")]
    [Row("using Composition = MyApp.Composition;\n[assembly: Composition.ApplicationModule]")]
    public void AQualifiedAssemblyAttributeOutsideTheEntryPointFile_IsReported(string bootstrap)
    {
        var result = RunWithEntryPoint(bootstrap, program: "System.Console.WriteLine();");

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0019");

        Verify(diagnostic.GetMessage().Contains("ApplicationModule"));
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0016"));
    }

    [VerifyTest]
    public void AQualifiedAssemblyAttributeInTheEntryPointFile_IsSilent()
    {
        var result = RunWithEntryPoint(
            bootstrap: "",
            program: """
            [assembly: MyApp.Composition.ApplicationModule]

            System.Console.WriteLine();
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0019"));
    }

    /// <summary>A qualified attribute that names no module is somebody else's.</summary>
    [VerifyTest]
    public void AQualifiedAttributeThatNamesNoModule_IsSilent()
    {
        var result = RunWithEntryPoint(
            bootstrap: "[assembly: System.Reflection.AssemblyMetadata(\"key\", \"value\")]",
            program: "System.Console.WriteLine();"
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0019"));
    }

    [VerifyTest]
    public void AnAssemblyAttributeInTheEntryPointFile_IsSilent()
    {
        var result = RunWithEntryPoint(
            bootstrap: "",
            program: """
            using MyApp.Composition;

            [assembly: ApplicationModule]

            System.Console.WriteLine();
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0019"));
    }

    /// <summary>
    /// The case that must stay quiet. A test project has no entry point of its own, so nothing
    /// generates an ApplicationModule and there is no file for the attribute to be in the wrong one
    /// relative to — and the test integration reads assembly attributes at run time anyway, which is
    /// what the testing guide shows.
    /// </summary>
    [VerifyTest]
    public void WithNoGeneratedApplicationModule_IsSilent()
    {
        var result = Run(
            """
            using MyApp.Composition;

            [assembly: ApplicationModule]
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0019"));
    }

    private static GeneratorResult Run(string bootstrap) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Module.cs"] = ModuleInNamespace,
                ["Bootstrap.cs"] = bootstrap,
            }
        );

    private static GeneratorResult RunWithEntryPoint(string bootstrap, string program) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Module.cs"] = ModuleInNamespace,
                ["Bootstrap.cs"] = bootstrap,
                ["Program.cs"] = program,
            }
        );
}

using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Utilities;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// The hint name of a generated file. Roslyn refuses a second file with the same name, and the run
/// then gives CS8785 and no generated code at all.
/// </summary>
public partial class FileNameHintTests
{
    [VerifyTest]
    [Row("App", "FooModule.Module.g.cs")]
    [Row("App.Sub", "Sub.FooModule.Module.g.cs")]
    [Row("App.global", "global.FooModule.Module.g.cs")]
    [Row("Sub", "global-Sub.FooModule.Module.g.cs")]
    [Row("Application", "global-Application.FooModule.Module.g.cs")]
    [Row("", "global-FooModule.Module.g.cs")]
    public void WithARootNamespace(string typeNamespace, string expected)
    {
        Verify(
            TypeDefinition.Get(typeNamespace, "FooModule").GetFileNameHint("App", "Module")
                == expected
        );
    }

    [VerifyTest]
    [Row("Sub", "Sub.FooModule.Module.g.cs")]
    [Row("", "FooModule.Module.g.cs")]
    public void WithoutARootNamespace_TheFullNameIsUsed(string typeNamespace, string expected)
    {
        Verify(
            TypeDefinition.Get(typeNamespace, "FooModule").GetFileNameHint("", "Module") == expected
        );
    }

    /// <summary>
    /// With the root namespace <c>App</c> removed, <c>App.Sub.FooModule</c> has the name of
    /// <c>Sub.FooModule</c>.
    /// </summary>
    [VerifyTest]
    public void ModulesThatDifferOnlyByTheRootNamespace_BothGetTheirCode()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Modules.cs"] = """
                using DependencyModules.Runtime.Attributes;

                namespace App.Sub
                {
                    [DependencyModule]
                    public partial class FooModule;
                }

                namespace Sub
                {
                    [DependencyModule]
                    public partial class FooModule;
                }

                namespace App
                {
                    [DependencyModule]
                    public partial class OtherModule;
                }
                """,
                ["Program.cs"] = """
                using DependencyModules.Runtime;
                using Microsoft.Extensions.DependencyInjection;

                new ServiceCollection()
                    .AddModule<App.OtherModule>()
                    .AddModule<App.Sub.FooModule>()
                    .AddModule<Sub.FooModule>();
                """,
            },
            new Dictionary<string, string> { ["RootNamespace"] = "App" },
            OutputKind.ConsoleApplication
        );

        result.AssertNoErrors();
        Verify(result.GeneratedSources.ContainsKey("Sub.FooModule.Module.g.cs"));
        Verify(result.GeneratedSources.ContainsKey("global-Sub.FooModule.Module.g.cs"));
    }
}

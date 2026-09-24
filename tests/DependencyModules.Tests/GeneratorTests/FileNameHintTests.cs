using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Utilities;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The hint name of a generated file. Roslyn refuses a second file with the same name, and the run
/// then gives CS8785 and no generated code at all.
/// </summary>
public class FileNameHintTests
{
    [Theory]
    [InlineData("App", "FooModule.Module.g.cs")]
    [InlineData("App.Sub", "Sub.FooModule.Module.g.cs")]
    [InlineData("App.global", "global.FooModule.Module.g.cs")]
    [InlineData("Sub", "global-Sub.FooModule.Module.g.cs")]
    [InlineData("Application", "global-Application.FooModule.Module.g.cs")]
    [InlineData("", "global-FooModule.Module.g.cs")]
    public void WithARootNamespace(string typeNamespace, string expected)
    {
        Assert.Equal(
            expected,
            TypeDefinition.Get(typeNamespace, "FooModule").GetFileNameHint("App", "Module")
        );
    }

    [Theory]
    [InlineData("Sub", "Sub.FooModule.Module.g.cs")]
    [InlineData("", "FooModule.Module.g.cs")]
    public void WithoutARootNamespace_TheFullNameIsUsed(string typeNamespace, string expected)
    {
        Assert.Equal(
            expected,
            TypeDefinition.Get(typeNamespace, "FooModule").GetFileNameHint("", "Module")
        );
    }

    /// <summary>
    /// With the root namespace <c>App</c> removed, <c>App.Sub.FooModule</c> has the name of
    /// <c>Sub.FooModule</c>.
    /// </summary>
    [Fact]
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
        Assert.Contains("Sub.FooModule.Module.g.cs", result.GeneratedSources.Keys);
        Assert.Contains("global-Sub.FooModule.Module.g.cs", result.GeneratedSources.Keys);
    }
}

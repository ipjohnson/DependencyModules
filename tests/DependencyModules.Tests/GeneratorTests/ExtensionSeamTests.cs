using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The seam a framework builds on: its own module attribute through <c>ModuleAttributeTypes()</c>,
/// its own attribute generators through <c>AttributeSourceGenerators()</c>. Both of these pin
/// behaviour a framework only finds out about in a consuming application, which is too late.
/// </summary>
public class ExtensionSeamTests {

    private const string FrameworkAttribute =
        """
        namespace Test.Framework;

        [System.AttributeUsage(System.AttributeTargets.Class)]
        public class FrameworkModuleAttribute : System.Attribute;
        """;

    private const string ModuleAndService =
        """
        using DependencyModules.Runtime;
        using DependencyModules.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        using Test.Framework;

        namespace TestNamespace;

        public interface IThing;

        [SingletonService]
        public class Thing : IThing;

        [FrameworkModule]
        public partial class AppModule;

        public static class Composition {
            public static IServiceCollection Compose() =>
                new ServiceCollection().AddModule<AppModule>();
        }
        """;

    /// <summary>
    /// A generator declaring its own module attribute gets the module partial without overriding
    /// <c>SetupRootGenerator</c>.
    /// </summary>
    /// <remarks>
    /// The call to <c>AddModule&lt;AppModule&gt;()</c> is the assertion that matters. Its constraint
    /// is <c>IDependencyModule, new()</c>, which the generated partial is what satisfies — so with
    /// no module emitted this compilation fails, exactly as the consuming application did while
    /// <c>SetupRootGenerator</c> was empty by default and easy to miss.
    /// </remarks>
    [Fact]
    public void FrameworkGenerator_EmitsTheModule_WithoutOverridingSetupRootGenerator() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Framework.cs"] = FrameworkAttribute,
                ["App.cs"] = ModuleAndService
            },
            generators: new ISourceGenerator[] { new FrameworkShapedGenerator().AsSourceGenerator() });

        result.AssertNoErrors();
        Assert.Contains("IDependencyModule", result.SourceContaining("AppModule.Module"));
    }

    /// <summary>
    /// A generator that only contributes providers opts out, and then nothing declares the module.
    /// </summary>
    [Fact]
    public void FrameworkGenerator_OptingOut_EmitsNoModule() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Framework.cs"] = FrameworkAttribute,
                ["App.cs"] = ModuleAndService
            },
            generators: new ISourceGenerator[] { new ProvidersOnlyGenerator().AsSourceGenerator() });

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("AppModule.Module"));
    }

    /// <summary>
    /// Stacking a framework generator on this package's own produces one ApplicationModule, not two.
    /// </summary>
    /// <remarks>
    /// <c>Program.cs</c> carries no module attribute, so nothing in the syntax distinguishes which
    /// generator it belongs to and both used to claim it — each emitting an ApplicationModule
    /// partial with the same members, which the compiler rejects. Stacking is the whole point of the
    /// extension seam, and a console application is the ordinary shape of a consumer, so the two
    /// have to work together.
    /// </remarks>
    [Fact]
    public void StackedGenerators_OverAConsoleApplication_EmitOneApplicationModule() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Framework.cs"] = FrameworkAttribute,
                ["Program.cs"] =
                    """
                    System.Console.WriteLine("hello");
                    """,
                ["App.cs"] = ModuleAndService
            },
            outputKind: OutputKind.ConsoleApplication,
            generators: new ISourceGenerator[] {
                new SourceGenerator.SourceGenerator().AsSourceGenerator(),
                new FrameworkShapedGenerator().AsSourceGenerator()
            });

        result.AssertNoErrors();

        Assert.Empty(result.DuplicateHintNames);
        Assert.Single(result.GeneratedSources.Keys, key => key.Contains("ApplicationModule.Module"));
    }

    /// <summary>
    /// The other extension shape: a generator adding registrations to <c>[DependencyModule]</c>
    /// modules rather than declaring modules of its own. Those partials belong to the generator
    /// this package ships, and writing them from both declares every module twice.
    /// </summary>
    [Fact]
    public void ThirdPartyGenerator_OnTheDefaultModuleAttribute_WritesNoModuleOfItsOwn() {
        var source =
            """
            using DependencyModules.Runtime;
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class AppModule;

            public static class Composition {
                public static IServiceCollection Compose() =>
                    new ServiceCollection().AddModule<AppModule>();
            }
            """;

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["App.cs"] = source },
            generators: new ISourceGenerator[] {
                new SourceGenerator.SourceGenerator().AsSourceGenerator(),
                new ThirdPartyGenerator().AsSourceGenerator()
            });

        result.AssertNoErrors();

        Assert.Empty(result.DuplicateHintNames);
        Assert.Single(result.GeneratedSources.Keys, key => key.Contains("AppModule.Module"));
    }

    /// <summary>
    /// What a framework declares: its module attribute, and the generators that read its own.
    /// </summary>
    private class FrameworkShapedGenerator : BaseSourceGenerator {

        protected override ITypeDefinition[] ModuleAttributeTypes() =>
            new[] { TypeDefinition.Get("Test.Framework", "FrameworkModuleAttribute") };

        protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
            yield return new global::DependencyModules.SourceGenerator.ServiceSourceGenerator();
        }
    }

    /// <summary>
    /// A generator taking the base class defaults, triggering on <c>[DependencyModule]</c>: the
    /// shape the extension guide documents.
    /// </summary>
    private class ThirdPartyGenerator : BaseSourceGenerator {

        protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
            yield break;
        }
    }

    private class ProvidersOnlyGenerator : FrameworkShapedGenerator {

        protected override void SetupRootGenerator(
            IncrementalGeneratorInitializationContext context,
            IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<(
                SourceGenerator.Impl.Models.ModuleEntryPointModel Left,
                SourceGenerator.Impl.Models.DependencyModuleConfigurationModel Right)>> valuesProvider) { }
    }
}

using DependencyModules.Runtime;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The environment attributes of a class that the generator reads as a symbol, not as a
/// declaration: a decorator that <c>[Decorate]</c> names, and a class that a convention finds in a
/// referenced assembly with <c>InAssemblyOf&lt;T&gt;()</c>.
/// </summary>
public class EnvironmentAttributesFromSymbolsTests
{
    private const string PluginLibrary = """
        using DependencyModules.Runtime.Attributes;

        namespace PluginPackage;

        public interface IPlugin { }

        public class AlwaysPlugin : IPlugin { }

        [IfEnvironment("Development")]
        public class DevelopmentPlugin : IPlugin { }

        [IfNotEnvironment("Development")]
        public class NotDevelopmentPlugin : IPlugin { }

        [IfEnvironmentValue("FEATURE_X", "on")]
        public class FeaturePlugin : IPlugin { }

        [IfNotEnvironmentValue("FEATURE_X")]
        public class NoFeaturePlugin : IPlugin { }
        """;

    private const string PluginModule = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;
        using PluginPackage;

        namespace TestNamespace;

        [DependencyModule]
        public partial class TestModule : IConventionModule
        {
            public void Conventions(IConventionDefinitions conventions)
            {
                conventions.RegisterAll<IPlugin>().InAssemblyOf<IPlugin>().AsSingleton();
            }
        }
        """;

    private static ModuleEnvironment Environment(string name, string? featureX = null) =>
        new(
            false,
            name,
            featureX == null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["FEATURE_X"] = featureX }
        );

    private static string Greet(GeneratedAssembly assembly)
    {
        var greeter = assembly.ResolveRequired("IGreeter");

        return (string)greeter.GetType().GetMethod("Greet")!.Invoke(greeter, null)!;
    }

    [Theory]
    [InlineData("Production", null, "AlwaysPlugin, NoFeaturePlugin, NotDevelopmentPlugin")]
    [InlineData("Development", "on", "AlwaysPlugin, DevelopmentPlugin, FeaturePlugin")]
    public void AClassFromAReferencedAssembly_KeepsItsEnvironmentAttributes(
        string environment,
        string? featureX,
        string expected
    )
    {
        var library = GeneratorTestHarness.CompileLibrary(
            PluginLibrary,
            "EnvironmentPluginPackage"
        );

        var assembly = GeneratedAssembly.Create(
            PluginModule,
            environment: Environment(environment, featureX),
            additionalReferences: new[] { library.Reference }
        );

        var registered = assembly
            .Services.Where(d => d.ServiceType.Name == "IPlugin")
            .Select(d => d.ImplementationType!.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(expected, string.Join(", ", registered));
    }

    [Theory]
    [InlineData("Production", "hello")]
    [InlineData("Development", "HELLO")]
    public void ADecoratorThatDecorateNames_KeepsItsEnvironmentAttribute(
        string environment,
        string expected
    )
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter
            {
                string Greet();
            }

            [SingletonService]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello";
            }

            [IfEnvironment("Development")]
            public class LoudGreeter(IGreeter inner) : IGreeter
            {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(LoudGreeter))]
            public partial class TestModule;
            """,
            environment: Environment(environment)
        );

        Assert.Equal(expected, Greet(assembly));
    }

    [Theory]
    [InlineData("Production", "hello")]
    [InlineData("Development", "HELLO")]
    public void ADecoratorFromAReferencedAssembly_KeepsItsEnvironmentAttribute(
        string environment,
        string expected
    )
    {
        var library = GeneratorTestHarness.CompileLibrary(
            """
            using DependencyModules.Runtime.Attributes;

            namespace DecoratorPackage;

            public interface IGreeter
            {
                string Greet();
            }

            [IfEnvironment("Development")]
            public class LoudGreeter : IGreeter
            {
                private readonly IGreeter _inner;

                public LoudGreeter(IGreeter inner) => _inner = inner;

                public string Greet() => _inner.Greet().ToUpperInvariant();
            }
            """,
            "EnvironmentDecoratorPackage"
        );

        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DecoratorPackage;

            namespace TestNamespace;

            [SingletonService]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello";
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(LoudGreeter))]
            public partial class TestModule;
            """,
            environment: Environment(environment),
            additionalReferences: new[] { library.Reference }
        );

        var greeter = assembly
            .BuildProvider()
            .GetService(library.Assembly.GetType("DecoratorPackage.IGreeter")!)!;

        Assert.Equal(expected, greeter.GetType().GetMethod("Greet")!.Invoke(greeter, null));
    }

    /// <summary>
    /// A condition that names nothing tests nothing, and the class would register in every
    /// environment. A class declared in the project gets DM0012 for this, so one read from metadata
    /// gets it too.
    /// </summary>
    [Fact]
    public void AnEmptyConditionOnAClassFromAReferencedAssembly_IsReported()
    {
        var library = GeneratorTestHarness.CompileLibrary(
            """
            using DependencyModules.Runtime.Attributes;

            namespace EmptyConditionPackage;

            public interface IPlugin { }

            [IfEnvironmentValue("")]
            public class EmptyKeyPlugin : IPlugin { }
            """,
            "EmptyConditionPluginPackage"
        );

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Test.cs"] = PluginModule.Replace("PluginPackage", "EmptyConditionPackage"),
            },
            additionalReferences: new[] { library.Reference }
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");
        Assert.Contains("EmptyKeyPlugin", diagnostic.GetMessage());
    }

    [Fact]
    public void AnEmptyConditionOnADecoratorThatDecorateNames_IsReported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter
            {
                string Greet();
            }

            [SingletonService]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello";
            }

            [IfEnvironment]
            public class LoudGreeter(IGreeter inner) : IGreeter
            {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(LoudGreeter))]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");
        Assert.Contains("LoudGreeter", diagnostic.GetMessage());
    }
}

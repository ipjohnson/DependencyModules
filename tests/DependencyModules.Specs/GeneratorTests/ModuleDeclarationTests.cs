using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// What the generator reads from a module declaration: its parameters, its equality members,
/// and whether it can complete the module at all.
/// </summary>
public class ModuleDeclarationTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        """;

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    [Fact]
    public void APropertyWithAPrivateSetter_IsNotAModuleParameter()
    {
        var result = Run(
            """
            [DependencyModule]
            public partial class TestModule
            {
                public string Label { get; private set; } = "shop";

                public string? Host { get; internal set; }
            }
            """
        );

        result.AssertNoErrors();

        var module = result.SourceContaining("Module.g.cs");

        Assert.DoesNotContain("newModule.Label", module);
        Assert.Contains("newModule.Host", module);
    }

    [Fact]
    public void APropertyOfANestedClass_IsNotAModuleParameter()
    {
        var result = Run(
            """
            [DependencyModule]
            public partial class TestModule
            {
                public class Options
                {
                    public int Size { get; set; }
                }
            }
            """
        );

        result.AssertNoErrors();

        Assert.DoesNotContain("newModule.Size", result.SourceContaining("Module.g.cs"));
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    [Fact]
    public void EqualsInAnotherPartialDeclaration_IsNotGeneratedAgain()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["TestModule.cs"] =
                    Preamble
                    + """
                    [DependencyModule]
                    public partial class TestModule
                    {
                        public string? Host { get; set; }
                    }
                    """,
                ["TestModule.Equality.cs"] = """
                namespace TestNamespace;

                public partial class TestModule
                {
                    public override bool Equals(object? obj) =>
                        obj is TestModule other && other.Host == Host;

                    public override int GetHashCode() => Host?.GetHashCode() ?? 0;
                }
                """,
            }
        );

        result.AssertNoErrors();

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    [Fact]
    public void AGetHashCodeOfItsOwn_IsNotGeneratedAgain()
    {
        var result = Run(
            """
            [DependencyModule]
            public partial class TestModule
            {
                public override int GetHashCode() => 7;
            }
            """
        );

        result.AssertNoErrors();

        var module = result.SourceContaining("Module.g.cs");

        Assert.Contains("override bool Equals", module);
        Assert.DoesNotContain("override int GetHashCode", module);
    }

    [Theory]
    [InlineData("other is not null", "a", "a", 1)]
    [InlineData("other is not null", "a", "b", 1)]
    [InlineData("other is not null && other.Host == Host", "a", "a", 1)]
    [InlineData("other is not null && other.Host == Host", "a", "b", 2)]
    public void AModuleWithOnlyTypedEquals_IsLoadedByThatEquals(
        string equality,
        string firstHost,
        string secondHost,
        int expectedLoads
    )
    {
        var source =
            Preamble
            + $$"""
                public record AuditMarker;

                [DependencyModule]
                public partial class TestModule : IServiceCollectionConfiguration, System.IEquatable<TestModule>
                {
                    public string? Host { get; set; }

                    public bool Equals(TestModule? other) => {{equality}};

                    public void ConfigureServices(IServiceCollection services) =>
                        services.AddSingleton(new AuditMarker());
                }
                """;

        Assert.DoesNotContain(
            GeneratorTestHarness.Run(source).GeneratorDiagnostics,
            d => d.Id == "DM0018"
        );

        var assembly = GeneratedAssembly.Create(source);

        var moduleType = assembly.Type("TestModule");

        IDependencyModule Module(string host)
        {
            var module = (IDependencyModule)Activator.CreateInstance(moduleType)!;
            moduleType.GetProperty("Host")!.SetValue(module, host);
            return module;
        }

        var services = new ServiceCollection().AddModules(Module(firstHost), Module(secondHost));

        Assert.Equal(
            expectedLoads,
            services.Count(d => d.ServiceType == assembly.Type("AuditMarker"))
        );
    }

    [Fact]
    public void AModuleThatIsNotPartial_GetsOnlyDM0003()
    {
        var result = Run(
            """
            public interface IClock { }

            [SingletonService]
            public class Clock : IClock { }

            [DependencyModule]
            public class ShopModule { }
            """
        );

        Assert.Equal("DM0003", Assert.Single(result.Errors).Id);
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("ShopModule"));
    }

    [Fact]
    public void ANestedModule_GetsOnlyDM0017()
    {
        var result = Run(
            """
            using DependencyModules.Runtime.Interception;

            public interface IClock
            {
                string Now();
            }

            public class PassInterceptor : IInterceptor
            {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) =>
                    context.Proceed();
            }

            [SingletonService]
            [Intercept(typeof(PassInterceptor))]
            public class Clock : IClock
            {
                public string Now() => "now";
            }

            [Decorator]
            public class LoggingClock(IClock inner) : IClock
            {
                public string Now() => inner.Now();
            }

            public static class Modules
            {
                [DependencyModule]
                public partial class ShopModule;
            }
            """
        );

        Assert.Equal("DM0017", Assert.Single(result.Errors).Id);
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("ShopModule"));
    }
}

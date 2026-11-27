using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// What the generator reads from a module declaration: its parameters, its equality members,
/// and whether it can complete the module at all.
/// </summary>
public partial class ModuleDeclarationTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        """;

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    [VerifyTest]
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

        Verify(!module.Contains("newModule.Label"));
        Verify(module.Contains("newModule.Host"));
    }

    [VerifyTest]
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

        Verify(!result.SourceContaining("Module.g.cs").Contains("newModule.Size"));
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    [VerifyTest]
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

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    [VerifyTest]
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

        Verify(module.Contains("override bool Equals"));
        Verify(!module.Contains("override int GetHashCode"));
    }

    [VerifyTest]
    [Row("other is not null", "a", "a", 1)]
    [Row("other is not null", "a", "b", 1)]
    [Row("other is not null && other.Host == Host", "a", "a", 1)]
    [Row("other is not null && other.Host == Host", "a", "b", 2)]
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

        Verify(!GeneratorTestHarness.Run(source).GeneratorDiagnostics.Any(d => d.Id == "DM0018"));

        var assembly = GeneratedAssembly.Create(source);

        var moduleType = assembly.Type("TestModule");

        IDependencyModule Module(string host)
        {
            var module = (IDependencyModule)Activator.CreateInstance(moduleType)!;
            moduleType.GetProperty("Host")!.SetValue(module, host);
            return module;
        }

        var services = new ServiceCollection().AddModules(Module(firstHost), Module(secondHost));

        Verify(services.Count(d => d.ServiceType == assembly.Type("AuditMarker")) == expectedLoads);
    }

    [VerifyTest]
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

        Verify(Single(result.Errors).Id == "DM0003");
        Verify(!result.GeneratedSources.Keys.Any(key => key.Contains("ShopModule")));
    }

    [VerifyTest]
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

        Verify(Single(result.Errors).Id == "DM0017");
        Verify(!result.GeneratedSources.Keys.Any(key => key.Contains("ShopModule")));
    }
}

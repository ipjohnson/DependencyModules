using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Whether generated code can use a type, a method or a constructor comes from its symbol. The
/// written keywords do not say it: a nested type or a member with no access modifier is private,
/// and a public member of a private type is out of reach too.
/// </summary>
public class GeneratedCodeAccessTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;

        namespace TestNamespace;

        """;

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    private static string ConventionModule(string convention) =>
        $$"""
            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    {{convention}}
                }
            }
            """;

    [Fact]
    public void AConvention_SkipsANestedClassThatGeneratedCodeCannotUse()
    {
        var result = Run(
            """
            public interface IRule { }

            public class RuleSet
            {
                class ImplicitlyPrivateRule : IRule { }

                protected class ProtectedRule : IRule { }

                protected internal class ProtectedInternalRule : IRule { }

                public class OpenRule : IRule { }

                private class Hidden
                {
                    public class PublicInPrivateRule : IRule { }
                }
            }

            """ + ConventionModule("conventions.RegisterAll<IRule>().AsSingleton();")
        );

        result.AssertNoErrors();

        var registrations = result.SourceContaining("ConventionDependencies");

        Assert.DoesNotContain("ImplicitlyPrivateRule", registrations);
        Assert.DoesNotContain("ProtectedRule)", registrations);
        Assert.DoesNotContain("PublicInPrivateRule", registrations);
        Assert.Contains("RuleSet.ProtectedInternalRule", registrations);
        Assert.Contains("RuleSet.OpenRule", registrations);
    }

    [Fact]
    public void AConventionBySelf_SkipsANestedClassWithNoAccessModifier()
    {
        var result = Run(
            """
            public class Workers
            {
                class HiddenWorker { }

                public class OpenWorker { }
            }

            """
                + ConventionModule(
                    """conventions.RegisterAll().WithName("*Worker").AsSelf().AsSingleton();"""
                )
        );

        result.AssertNoErrors();

        var registrations = result.SourceContaining("ConventionDependencies");

        Assert.DoesNotContain("HiddenWorker", registrations);
        Assert.Contains("Workers.OpenWorker", registrations);
    }

    [Fact]
    public void AConvention_SkipsAFileLocalClass()
    {
        var result = Run(
            """
            public interface IRule { }

            file class FileRule : IRule { }

            public class OpenRule : IRule { }

            """ + ConventionModule("conventions.RegisterAll<IRule>().AsSingleton();")
        );

        result.AssertNoErrors();

        var registrations = result.SourceContaining("ConventionDependencies");

        Assert.DoesNotContain("FileRule", registrations);
        Assert.Contains("OpenRule", registrations);
    }

    [Fact]
    public void AFactoryMethodWithNoAccessModifier_IsReported_AndNotCalled()
    {
        var result = Run(
            """
            public interface IClock { }

            public class Clock : IClock { }

            public static class ClockFactories
            {
                [SingletonService]
                static IClock CreateClock() => new Clock();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Assert.Contains("'ClockFactories.CreateClock'", diagnostic.GetMessage());
        Assert.DoesNotContain(result.GeneratedSources.Values, s => s.Contains("CreateClock"));
    }

    [Theory]
    [InlineData("private static", "private or protected")]
    [InlineData("protected static", "private or protected")]
    [InlineData("private protected static", "private or protected")]
    [InlineData("public", "not static")]
    public void AFactoryMethodTheModuleCannotCall_IsReported(string modifiers, string reason)
    {
        var result = Run(
            $$"""
            public interface ITimer { }

            public class Timer : ITimer { }

            public class TimerFactories
            {
                [SingletonService]
                {{modifiers}} ITimer CreateTimer() => new Timer();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.DoesNotContain(result.GeneratedSources.Values, s => s.Contains("CreateTimer"));
    }

    [Fact]
    public void AFactoryMethodInAPrivateClass_IsReported()
    {
        var result = Run(
            """
            public interface ITimer { }

            public class Timer : ITimer { }

            public class Outer
            {
                private static class Factories
                {
                    [SingletonService]
                    public static ITimer CreateTimer() => new Timer();
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Assert.Contains("'Outer.Factories.CreateTimer'", diagnostic.GetMessage());
    }

    [Fact]
    public void AnInternalFactoryMethodInAnInternalClass_IsCalled()
    {
        var assembly = GeneratedAssembly.Create(
            Preamble
                + """
                public interface ITimer { }

                public class Timer : ITimer { }

                internal static class TimerFactories
                {
                    [SingletonService]
                    internal static ITimer CreateTimer() => new Timer();
                }

                [DependencyModule]
                public partial class TestModule;
                """
        );

        Assert.Equal("Timer", assembly.ResolveRequired("ITimer").GetType().Name);
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    public void AConventionClassWithOnlyAnInternalConstructor_IsReported(string access)
    {
        var result = Run(
            $$"""
            public interface IHandler { }

            public class Handler : IHandler
            {
                {{access}} Handler() { }
            }

            """ + ConventionModule("conventions.RegisterAll<IHandler>().AsSingleton();")
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Assert.Contains("no public constructor", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.GeneratedSources.Values,
            s => s.Contains("typeof(global::TestNamespace.Handler)")
        );
    }

    /// <summary>
    /// The first declaration has no constructor, so its syntax alone says the compiler adds a
    /// public one.
    /// </summary>
    [Fact]
    public void AnInternalConstructorOnAnotherPartialDeclaration_IsReported()
    {
        var result = Run(
            """
            public partial class Worker { }

            public partial class Worker
            {
                internal Worker() { }
            }

            """
                + ConventionModule(
                    """conventions.RegisterAll().WithName("Worker").AsSelf().AsSingleton();"""
                )
        );

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Assert.Contains("'Worker'", diagnostic.GetMessage());
    }

    [Fact]
    public void AModuleThatGeneratesFactories_RegistersAClassWithAnInternalConstructor()
    {
        var assembly = GeneratedAssembly.Create(
            Preamble
                + """
                public interface IHandler { }

                public class Handler : IHandler
                {
                    internal Handler() { }
                }

                [DependencyModule(GenerateFactories = true)]
                public partial class TestModule : IConventionModule
                {
                    public void Conventions(IConventionDefinitions conventions)
                    {
                        conventions.RegisterAll<IHandler>().AsSingleton();
                    }
                }
                """
        );

        Assert.Equal("Handler", assembly.ResolveRequired("IHandler").GetType().Name);
    }

    [Fact]
    public void AModuleThatGeneratesFactories_StillReportsAPrivateConstructor()
    {
        var result = Run(
            """
            public interface IHandler { }

            public class Handler : IHandler
            {
                private Handler() { }
            }

            [DependencyModule(GenerateFactories = true)]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IHandler>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Assert.Contains("no public or internal constructor", diagnostic.GetMessage());
    }
}

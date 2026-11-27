using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Whether generated code can use a type, a method or a constructor comes from its symbol. The
/// written keywords do not say it: a nested type or a member with no access modifier is private,
/// and a public member of a private type is out of reach too.
/// </summary>
public partial class GeneratedCodeAccessTests
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

    [VerifyTest]
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

        Verify(!registrations.Contains("ImplicitlyPrivateRule"));
        Verify(!registrations.Contains("ProtectedRule)"));
        Verify(!registrations.Contains("PublicInPrivateRule"));
        Verify(registrations.Contains("RuleSet.ProtectedInternalRule"));
        Verify(registrations.Contains("RuleSet.OpenRule"));
    }

    [VerifyTest]
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

        Verify(!registrations.Contains("HiddenWorker"));
        Verify(registrations.Contains("Workers.OpenWorker"));
    }

    [VerifyTest]
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

        Verify(!registrations.Contains("FileRule"));
        Verify(registrations.Contains("OpenRule"));
    }

    [VerifyTest]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Verify(diagnostic.GetMessage().Contains("'ClockFactories.CreateClock'"));
        Verify(!result.GeneratedSources.Values.Any(s => s.Contains("CreateClock")));
    }

    [VerifyTest]
    [Row("private static", "private or protected")]
    [Row("protected static", "private or protected")]
    [Row("private protected static", "private or protected")]
    [Row("public", "not static")]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Verify(diagnostic.GetMessage().Contains(reason));
        Verify(!result.GeneratedSources.Values.Any(s => s.Contains("CreateTimer")));
    }

    [VerifyTest]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0023");
        Verify(diagnostic.GetMessage().Contains("'Outer.Factories.CreateTimer'"));
    }

    [VerifyTest]
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

        Verify(assembly.ResolveRequired("ITimer").GetType().Name == "Timer");
    }

    [VerifyTest]
    [Row("internal")]
    [Row("protected internal")]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Verify(diagnostic.GetMessage().Contains("no public constructor"));
        Verify(
            !result.GeneratedSources.Values.Any(s =>
                s.Contains("typeof(global::TestNamespace.Handler)")
            )
        );
    }

    /// <summary>
    /// The first declaration has no constructor, so its syntax alone says the compiler adds a
    /// public one.
    /// </summary>
    [VerifyTest]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Verify(diagnostic.GetMessage().Contains("'Worker'"));
    }

    [VerifyTest]
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

        Verify(assembly.ResolveRequired("IHandler").GetType().Name == "Handler");
    }

    [VerifyTest]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");
        Verify(diagnostic.GetMessage().Contains("no public or internal constructor"));
    }
}

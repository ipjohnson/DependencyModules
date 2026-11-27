using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// A <c>[Decorator]</c> that the generator cannot apply is reported as DM0025, and one that gets its
/// service type through a base class or a derived interface is applied.
/// </summary>
public partial class IgnoredDecoratorTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;

        namespace TestNamespace;

        public interface IClock
        {
            string Now();
        }

        [SingletonService]
        public class Clock : IClock
        {
            public string Now() => "now";
        }

        [DependencyModule]
        public partial class TestModule;

        """;

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    private static string Now(GeneratedAssembly assembly)
    {
        var clock = assembly.ResolveRequired("IClock");

        return (string)clock.GetType().GetMethod("Now")!.Invoke(clock, null)!;
    }

    [VerifyTest]
    [Row("protected")]
    [Row("private")]
    [Row("private protected")]
    [Row("")]
    public void ADecoratorThatGeneratedCodeCannotConstruct_IsReported(string access)
    {
        var result = Run(
            $$"""
            [Decorator]
            public class LoggingClock : IClock
            {
                private readonly IClock _inner;

                {{access}} LoggingClock(IClock inner) => _inner = inner;

                public string Now() => _inner.Now();
            }
            """
        );

        result.AssertNoErrors();

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Verify(diagnostic.GetMessage().Contains("'LoggingClock'"));
        Verify(diagnostic.GetMessage().Contains("no constructor that generated code can call"));
        Verify(
            !result.GeneratedSources.Values.Any(s =>
                s.Contains("new global::TestNamespace.LoggingClock")
            )
        );
    }

    [VerifyTest]
    [Row("internal")]
    [Row("protected internal")]
    public void ADecoratorWithAnInternalConstructor_IsApplied(string access)
    {
        var assembly = GeneratedAssembly.Create(
            Preamble
                + $$"""
                [Decorator]
                public class LoggingClock : IClock
                {
                    private readonly IClock _inner;

                    {{access}} LoggingClock(IClock inner) => _inner = inner;

                    public string Now() => $"logged({_inner.Now()})";
                }
                """
        );

        Verify(Now(assembly) == "logged(now)");
    }

    [VerifyTest]
    public void ADecoratorThatGetsItsServiceFromABaseClass_IsApplied()
    {
        var assembly = GeneratedAssembly.Create(
            Preamble
                + """
                public abstract class ClockBase : IClock
                {
                    public abstract string Now();
                }

                [Decorator]
                public class LoudClock(IClock inner) : ClockBase
                {
                    public override string Now() => inner.Now().ToUpperInvariant();
                }
                """
        );

        Verify(Now(assembly) == "NOW");
    }

    [VerifyTest]
    public void ADecoratorThatGetsItsServiceFromADerivedInterface_IsApplied()
    {
        var assembly = GeneratedAssembly.Create(
            Preamble
                + """
                public interface ITimedClock : IClock { }

                [Decorator]
                public class LoggingClock(IClock inner) : ITimedClock
                {
                    public string Now() => $"logged({inner.Now()})";
                }
                """
        );

        Verify(Now(assembly) == "logged(now)");
    }

    [VerifyTest]
    public void ADecoratorWithNoService_IsReported()
    {
        var result = Run(
            """
            [Decorator]
            public class Stopwatch(IClock clock)
            {
                public string Read() => clock.Now();
            }
            """
        );

        result.AssertNoErrors();

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Verify(diagnostic.GetMessage().Contains("'Stopwatch'"));
        Verify(diagnostic.GetMessage().Contains("no service to decorate"));
    }

    [VerifyTest]
    public void ADecoratorWhoseConstructorDoesNotTakeItsService_IsReported()
    {
        var result = Run(
            """
            public interface ITimer
            {
                string Now();
            }

            [Decorator(Service = typeof(IClock))]
            public class TimerClock(ITimer timer) : IClock
            {
                public string Now() => timer.Now();
            }
            """
        );

        result.AssertNoErrors();

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Verify(diagnostic.GetMessage().Contains("'TimerClock'"));
        Verify(diagnostic.GetMessage().Contains("'IClock'"));
    }

    [VerifyTest]
    public void AGenericDecoratorWithReorderedTypeParameters_IsReported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<TIn, TOut>
            {
                TOut Handle(TIn input);
            }

            [SingletonService]
            public class Handler : IHandler<int, string>
            {
                public string Handle(int input) => input.ToString();
            }

            [Decorator]
            public class Swapped<TOut, TIn>(IHandler<TIn, TOut> inner) : IHandler<TIn, TOut>
            {
                public TOut Handle(TIn input) => inner.Handle(input);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Verify(diagnostic.GetMessage().Contains("'Swapped'"));
        Verify(diagnostic.GetMessage().Contains("type parameters"));
    }

    [VerifyTest]
    public void ADecoratorThatCanBeApplied_IsNotReported()
    {
        var result = Run(
            """
            [Decorator]
            public class LoggingClock(IClock inner) : IClock
            {
                public string Now() => inner.Now();
            }
            """
        );

        result.AssertNoErrors();
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0025"));
    }
}

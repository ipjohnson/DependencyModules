using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// A <c>[Decorator]</c> that the generator cannot apply is reported as DM0025, and one that gets its
/// service type through a base class or a derived interface is applied.
/// </summary>
public class IgnoredDecoratorTests
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

    [Theory]
    [InlineData("protected")]
    [InlineData("private")]
    [InlineData("private protected")]
    [InlineData("")]
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

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Assert.Contains("'LoggingClock'", diagnostic.GetMessage());
        Assert.Contains("no constructor that generated code can call", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.GeneratedSources.Values,
            s => s.Contains("new global::TestNamespace.LoggingClock")
        );
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
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

        Assert.Equal("logged(now)", Now(assembly));
    }

    [Fact]
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

        Assert.Equal("NOW", Now(assembly));
    }

    [Fact]
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

        Assert.Equal("logged(now)", Now(assembly));
    }

    [Fact]
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

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Assert.Contains("'Stopwatch'", diagnostic.GetMessage());
        Assert.Contains("no service to decorate", diagnostic.GetMessage());
    }

    [Fact]
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

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Assert.Contains("'TimerClock'", diagnostic.GetMessage());
        Assert.Contains("'IClock'", diagnostic.GetMessage());
    }

    [Fact]
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

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0025");
        Assert.Contains("'Swapped'", diagnostic.GetMessage());
        Assert.Contains("type parameters", diagnostic.GetMessage());
    }

    [Fact]
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
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0025");
    }
}

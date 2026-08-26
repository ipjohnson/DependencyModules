using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// An interceptor's wrapper is generated from one class and forwards that class's members, so it
/// belongs to that class's registration and no other.
///
/// It used to be applied to <i>every</i> registration of the service type, because interception
/// reuses the decorator rewrite and a decorator is declared against an interface — where wrapping
/// everything behind it is correct. The symptoms were an implementation carrying no
/// <c>[Intercept]</c> coming back wrapped in another class's wrapper, and, with two implementations
/// marked, every interceptor running twice per call. Neither threw.
/// </summary>
public class InterceptionScopeTests {

    private const string Interceptor =
        """
        public sealed class CountingInterceptor : IInterceptor {
            public static int Calls;
            public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                Calls++;
                return context.Proceed();
            }
        }
        """;

    [Fact]
    public void AnUnmarkedSiblingImplementation_IsNotWrapped() {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                [SingletonService]
                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """));

        var provider = generated.BuildProvider();

        var resolved = ((System.Collections.IEnumerable)provider
                .GetService(typeof(System.Collections.Generic.IEnumerable<>)
                    .MakeGenericType(generated.Type("IGreeter")))!)
            .Cast<object>()
            .Select(g => g.GetType().Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "Loud_Intercepted", "Quiet" }, resolved);
    }

    [Fact]
    public void TwoMarkedImplementations_EachGetTheirOwnWrapper() {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """));

        var provider = generated.BuildProvider();

        var resolved = ((System.Collections.IEnumerable)provider
                .GetService(typeof(System.Collections.Generic.IEnumerable<>)
                    .MakeGenericType(generated.Type("IGreeter")))!)
            .Cast<object>()
            .Select(g => g.GetType().Name)
            .OrderBy(n => n)
            .ToArray();

        // Each behind its own wrapper, rather than both behind whichever was emitted last.
        Assert.Equal(new[] { "Loud_Intercepted", "Quiet_Intercepted" }, resolved);
    }

    /// <summary>
    /// The registration is rewritten into a factory by the decorator, which erases the
    /// implementation type from the descriptor. An interceptor ordered outside it has to recognise
    /// its own registration anyway, or narrowing the rewrite would silently stop intercepting a
    /// service that had asked for it.
    /// </summary>
    [Fact]
    public void AnInterceptorOrderedOutsideADecorator_StillApplies() {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor), Order = 2000)]
                public sealed class Core : IGreeter { public string Greet() => "core"; }

                [Decorator(Order = 1000)]
                public sealed class Bracketed(IGreeter inner) : IGreeter {
                    public string Greet() => "[" + inner.Greet() + "]";
                }
                """));

        var resolved = generated.ResolveRequired("IGreeter");

        Assert.Equal("Core_Intercepted", resolved.GetType().Name);

        // The decorator is still inside the interception wrapper, so both ran.
        var greet = (string)resolved.GetType().GetMethod("Greet")!.Invoke(resolved, null)!;

        Assert.Equal("[core]", greet);
    }

    private static string Source(string body) =>
        $$"""
          using DependencyModules.Runtime.Attributes;
          using DependencyModules.Runtime.Interception;

          namespace TestNamespace;

          public interface IGreeter { string Greet(); }

          {{Interceptor}}

          {{body}}

          [DependencyModule]
          public partial class TestModule;
          """;
}

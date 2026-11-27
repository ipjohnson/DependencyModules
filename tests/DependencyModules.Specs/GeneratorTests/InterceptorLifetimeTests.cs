using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// The lifetime an interceptor is registered with.
///
/// It was always <c>TryAddSingleton</c>, and that is the only place an interceptor type is
/// registered — so an interceptor taking a scoped dependency became a captive dependency, held for
/// the life of the container by a singleton that should not have outlived the scope. Silent unless
/// <c>ValidateScopes</c> is on, and <c>GenerateFactories</c> switches that off for the whole
/// project.
///
/// The escape hatch was to put a service attribute on the interceptor so the <c>TryAdd</c> found a
/// registration already there and yielded — which works, because services are applied before
/// decorators, but only somebody reading the generated code would ever find it. Naming the lifetime
/// where the interception is declared says it out loud.
/// </summary>
public partial class InterceptorLifetimeTests
{
    [VerifyTest]
    public void WithNoLifetimeNamed_TheInterceptorIsASingleton()
    {
        var interceptors = Run("[Intercept(typeof(CountingInterceptor))]");

        Verify(interceptors.Contains("TryAddSingleton"));
    }

    [VerifyTest]
    [Row("Scoped", "TryAddScoped")]
    [Row("Transient", "TryAddTransient")]
    [Row("Singleton", "TryAddSingleton")]
    public void ANamedLifetime_IsTheOneRegistered(string lifetime, string expected)
    {
        var interceptors = Run(
            $"[Intercept(typeof(CountingInterceptor), Lifetime = ServiceLifetime.{lifetime})]"
        );

        Verify(interceptors.Contains(expected));
    }

    /// <summary>
    /// A scoped interceptor must not also be registered as a singleton. The point is to choose,
    /// not to add a second registration the container resolves ahead of the first.
    /// </summary>
    [VerifyTest]
    public void AScopedInterceptor_IsNotAlsoRegisteredAsASingleton()
    {
        var interceptors = Run(
            "[Intercept(typeof(CountingInterceptor), Lifetime = ServiceLifetime.Scoped)]"
        );

        Verify(!interceptors.Contains("TryAddSingleton"));
    }

    /// <summary>
    /// The registration still has to work end to end, not merely be emitted with the right name.
    /// </summary>
    [VerifyTest]
    public void AScopedInterceptor_Runs()
    {
        var generated = GeneratedAssembly.Create(
            Source("[Intercept(typeof(CountingInterceptor), Lifetime = ServiceLifetime.Scoped)]")
        );

        var provider = generated.BuildProvider();
        var greeter = provider.GetService(generated.Type("IGreeter"))!;

        Verify(greeter.GetType().Name == "Greeter_Intercepted");
        Verify(Equals(greeter.GetType().GetMethod("Greet")!.Invoke(greeter, null), "hi"));
    }

    private static string Run(string attribute) =>
        GeneratorTestHarness
            .Run(Source(attribute))
            .AssertNoErrors()
            .SourceContaining("Interceptors");

    private static string Source(string attribute) =>
        $$"""
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public sealed class CountingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
            }

            [SingletonService]
            {{attribute}}
            public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

            [DependencyModule]
            public partial class TestModule;
            """;
}

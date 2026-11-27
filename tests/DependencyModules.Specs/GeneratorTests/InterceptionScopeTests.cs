using System.Linq;
using DependencyModules.Runtime;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

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
public partial class InterceptionScopeTests
{
    private const string Interceptor = """
        public sealed class CountingInterceptor : IInterceptor {
            public static int Calls;
            public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                Calls++;
                return context.Proceed();
            }
        }
        """;

    [VerifyTest]
    public void AnUnmarkedSiblingImplementation_IsNotWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                [SingletonService]
                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """
            )
        );

        var provider = generated.BuildProvider();

        var resolved = (
            (System.Collections.IEnumerable)
                provider.GetService(
                    typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(
                        generated.Type("IGreeter")
                    )
                )!
        )
            .Cast<object>()
            .Select(g => g.GetType().Name)
            .OrderBy(n => n)
            .ToArray();

        Verify(resolved.SequenceEqual(new[] { "Loud_Intercepted", "Quiet" }));
    }

    [VerifyTest]
    public void TwoMarkedImplementations_EachGetTheirOwnWrapper()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """
            )
        );

        var provider = generated.BuildProvider();

        var resolved = (
            (System.Collections.IEnumerable)
                provider.GetService(
                    typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(
                        generated.Type("IGreeter")
                    )
                )!
        )
            .Cast<object>()
            .Select(g => g.GetType().Name)
            .OrderBy(n => n)
            .ToArray();

        // Each behind its own wrapper, rather than both behind whichever was emitted last.
        Verify(resolved.SequenceEqual(new[] { "Loud_Intercepted", "Quiet_Intercepted" }));
    }

    /// <summary>
    /// The registration is rewritten into a factory by the decorator, which erases the
    /// implementation type from the descriptor. An interceptor ordered outside it has to recognise
    /// its own registration anyway, or narrowing the rewrite would silently stop intercepting a
    /// service that had asked for it.
    /// </summary>
    [VerifyTest]
    public void AnInterceptorOrderedOutsideADecorator_StillApplies()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor), Order = 2000)]
                public sealed class Core : IGreeter { public string Greet() => "core"; }

                [Decorator(Order = 1000)]
                public sealed class Bracketed(IGreeter inner) : IGreeter {
                    public string Greet() => "[" + inner.Greet() + "]";
                }
                """
            )
        );

        var resolved = generated.ResolveRequired("IGreeter");

        Verify(resolved.GetType().Name == "Core_Intercepted");

        // The decorator is still inside the interception wrapper, so both ran.
        var greet = (string)resolved.GetType().GetMethod("Greet")!.Invoke(resolved, null)!;

        Verify(greet == "[core]");
    }

    /// <summary>
    /// The repro from issue #80. A registration made from a factory or an instance of another class
    /// is not the one the wrapper was generated from.
    /// </summary>
    [VerifyTest]
    public void AFactoryOrAnInstanceOfAnotherClass_IsNotWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }

                public sealed class Plain : IGreeter { public string Greet() => "plain"; }
                """,
                """
                services.AddSingleton<IGreeter>(_ => new Quiet());
                services.AddSingleton<IGreeter>(new Plain());
                """
            )
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud_Intercepted", "Plain", "Quiet" }));
    }

    /// <summary>
    /// Every shape of registration the generator writes for an intercepted class carries the class
    /// as its implementation. A cross-wired interface is a factory that resolves the class, so
    /// that factory is typed to return the class.
    /// </summary>
    [VerifyTest]
    [Row("[SingletonService]")]
    [Row("[SingletonService(Using = RegistrationType.Try)]")]
    [Row("[SingletonService(Using = RegistrationType.Replace)]")]
    [Row("[SingletonService(Using = RegistrationType.TryEnumerable)]")]
    [Row("[SingletonService] [IfEnvironment(\"Development\")]")]
    [Row("[CrossWireService]")]
    [Row("[CrossWireService(Using = RegistrationType.TryEnumerable)]")]
    public void OnlyTheRegistrationOfTheInterceptedClass_IsWrapped(string registration)
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                $$"""
                {{registration}} [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """,
                "services.AddSingleton<IGreeter>(_ => new Quiet());"
            ),
            environment: new ModuleEnvironment("Development")
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud_Intercepted", "Quiet" }));
    }

    [VerifyTest]
    public void OnlyTheKeyedRegistrationOfTheInterceptedClass_IsWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                """
                [SingletonService(Key = "k")] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
                """,
                """services.AddKeyedSingleton<IGreeter>("k", (_, _) => new Quiet());"""
            )
        );

        Verify(Greeters(generated, "k").SequenceEqual(new[] { "Loud_Intercepted", "Quiet" }));
    }

    [VerifyTest]
    public void AConventionThatCrossWiresTheInterceptedClass_StillWrapsIt()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;
            using DependencyModules.Runtime.Interception;
            using DependencyModules.Runtime.Interfaces;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            {{Interceptor}}

            [Intercept(typeof(CountingInterceptor))]
            public sealed class Loud : IGreeter { public string Greet() => "loud"; }

            public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }

            [DependencyModule]
            public partial class TestModule : IConventionModule, IServiceCollectionConfiguration {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().WithName("Loud").AlsoAsSelf().AsSingleton();
                }

                public void ConfigureServices(IServiceCollection services) {
                    services.AddSingleton<IGreeter>(_ => new Quiet());
                }
            }
            """
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud_Intercepted", "Quiet" }));
    }

    /// <summary>
    /// The registration of a factory method is typed to the class the method returns.
    /// </summary>
    [VerifyTest]
    public void AFactoryMethodThatReturnsTheInterceptedClass_IsWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                """
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }

                public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }

                public static class Factories {
                    [SingletonService(As = typeof(IGreeter))]
                    public static Loud CreateLoud() => new Loud();

                    [SingletonService]
                    public static IGreeter CreateQuiet() => new Quiet();
                }
                """,
                ""
            )
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud_Intercepted", "Quiet" }));
    }

    /// <summary>
    /// The container reads an instance's own type as its implementation, and so does interception.
    /// </summary>
    [VerifyTest]
    public void AnInstanceOfTheInterceptedClass_IsWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }
                """,
                "services.AddSingleton<IGreeter>(new Loud());"
            )
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud_Intercepted", "Loud_Intercepted" }));
    }

    /// <summary>
    /// A factory typed to return the service names no implementation, so it is not wrapped even when
    /// it builds the intercepted class. The container cannot tell what it builds either.
    /// </summary>
    [VerifyTest]
    public void AFactoryThatReturnsTheService_IsNotWrapped()
    {
        var generated = GeneratedAssembly.Create(
            Configured(
                """
                [SingletonService] [Intercept(typeof(CountingInterceptor))]
                public sealed class Loud : IGreeter { public string Greet() => "loud"; }
                """,
                "services.AddSingleton<IGreeter>(_ => new Loud());"
            )
        );

        Verify(Greeters(generated).SequenceEqual(new[] { "Loud", "Loud_Intercepted" }));
    }

    /// <summary>
    /// The type names of every <c>IGreeter</c> the provider resolves, sorted.
    /// </summary>
    private static string[] Greeters(GeneratedAssembly generated, object? key = null)
    {
        var provider = generated.BuildProvider();
        var greeter = generated.Type("IGreeter");

        var resolved =
            key == null
                ? (System.Collections.IEnumerable)
                    provider.GetService(
                        typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(greeter)
                    )!
                : provider.GetKeyedServices(greeter, key);

        return resolved.Cast<object>().Select(g => g.GetType().Name).OrderBy(n => n).ToArray();
    }

    /// <summary>
    /// A module whose ConfigureServices adds registrations by hand, after the generated ones.
    /// </summary>
    private static string Configured(string body, string configure) =>
        $$"""
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;
            using DependencyModules.Runtime.Interfaces;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            {{Interceptor}}

            {{body}}

            [DependencyModule]
            public partial class TestModule : IServiceCollectionConfiguration {
                public void ConfigureServices(IServiceCollection services) {
                    {{configure}}
                }
            }
            """;

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

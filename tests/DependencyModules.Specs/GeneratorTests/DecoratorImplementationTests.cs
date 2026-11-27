using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// <c>[Decorator(Implementation = typeof(X))]</c> — decorating one implementation rather than every
/// registration of the service.
///
/// A decorator is declared against an interface, and wrapping everything behind it is the right
/// default: that is what a decorator is for, and what separates it from interception. But a project
/// with several implementations of one interface had no way to say "wrap this one" — the report's
/// agents accepted decoration of every registration because there was nothing else to write.
///
/// This is the inverse of the change 1.1.0 made to interception, which went from per-service to
/// per-implementation. The runtime already had what it needs: DecoratorHelper.Decorate takes an
/// implementation type and skips descriptors built from anything else, which is the overload
/// interception uses. Only the attribute could not say so.
/// </summary>
public partial class DecoratorImplementationTests
{
    /// <summary>
    /// The default, unchanged: no Implementation named, so every registration of the service is
    /// wrapped.
    /// </summary>
    [VerifyTest]
    public void WithNoImplementationNamed_EveryRegistrationIsWrapped()
    {
        var resolved = Resolve("[Decorator]");

        Verify(resolved.Select(Outer).SequenceEqual(["Logged", "Logged"]));
    }

    [VerifyTest]
    public void NamingAnImplementation_WrapsOnlyThatOne()
    {
        var resolved = Resolve("[Decorator(Implementation = typeof(Loud))]");

        Verify(resolved.Select(Outer).SequenceEqual(["Logged", "Quiet"]));
    }

    /// <summary>
    /// And the one it wrapped is still the one it wrapped — the decorator holds the named
    /// implementation, not whichever registration happened to be first.
    /// </summary>
    [VerifyTest]
    public void TheWrappedInstance_IsTheNamedImplementation()
    {
        var resolved = Resolve("[Decorator(Implementation = typeof(Loud))]");

        var logged = Single(resolved, greeter => Outer(greeter) == "Logged");

        Verify(Greet(logged) == "loud");
    }

    [VerifyTest]
    public void TheUnnamedImplementation_IsUntouched()
    {
        var resolved = Resolve("[Decorator(Implementation = typeof(Loud))]");

        var quiet = Single(resolved, greeter => Outer(greeter) == "Quiet");

        Verify(Greet(quiet) == "quiet");
    }

    /// <summary>
    /// Naming an implementation nothing registers wraps nothing, rather than falling back to
    /// wrapping everything — the failure mode that would make a typo silently behave like the
    /// default.
    /// </summary>
    [VerifyTest]
    public void NamingAnImplementationThatIsNotRegistered_WrapsNothing()
    {
        var resolved = Resolve("[Decorator(Implementation = typeof(Unregistered))]");

        Verify(resolved.Select(Outer).SequenceEqual(["Loud", "Quiet"]));
    }

    /// <summary>
    /// A factory from <c>DependencyModules_GenerateFactories</c> is typed to return its class, so
    /// the decorator tells the registrations apart as it does without the property.
    /// </summary>
    [VerifyTest]
    public void NamingAnImplementationUnderGenerateFactories_WrapsOnlyThatOne()
    {
        var resolved = Resolve(
            "[Decorator(Implementation = typeof(Loud))]",
            generateFactories: true
        );

        Verify(resolved.Select(Outer).SequenceEqual(["Logged", "Quiet"]));
    }

    [VerifyTest]
    public void NamingNoImplementationUnderGenerateFactories_WrapsEveryRegistration()
    {
        var resolved = Resolve("[Decorator]", generateFactories: true);

        Verify(resolved.Select(Outer).SequenceEqual(["Logged", "Logged"]));
    }

    private static string Outer(object greeter) => greeter.GetType().Name;

    private static string Greet(object greeter) =>
        (string)greeter.GetType().GetMethod("Greet")!.Invoke(greeter, null)!;

    private static object[] Resolve(string decoratorAttribute, bool generateFactories = false)
    {
        var generated = GeneratedAssembly.Create(
            Source(decoratorAttribute),
            buildProperties: new Dictionary<string, string>
            {
                ["DependencyModules_GenerateFactories"] = generateFactories ? "true" : "false",
            }
        );

        var provider = generated.BuildProvider();

        return (
            (System.Collections.IEnumerable)
                provider.GetService(
                    typeof(IEnumerable<>).MakeGenericType(generated.Type("IGreeter"))
                )!
        )
            .Cast<object>()
            .OrderBy(greeter => greeter.GetType().Name, System.StringComparer.Ordinal)
            .ToArray();
    }

    private static string Source(string decoratorAttribute) =>
        $$"""
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Loud : IGreeter { public string Greet() => "loud"; }

            [SingletonService]
            public class Quiet : IGreeter { public string Greet() => "quiet"; }

            public class Unregistered : IGreeter { public string Greet() => "nowhere"; }

            {{decoratorAttribute}}
            public class Logged(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }

            [DependencyModule]
            public partial class TestModule;
            """;
}

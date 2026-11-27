using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Every legal way of writing <c>[Intercept]</c> has to mean <c>[Intercept]</c>.
///
/// 1.1.0 fixed this for the service attributes: the usage is resolved through the semantic model
/// rather than compared as written, so a qualified name, a <c>global::</c> prefix and a using alias
/// all land on the same attribute. Before that they were discovered but read back wrong, and the
/// registration was silently the wrong lifetime.
///
/// <c>[Intercept]</c> never got the same treatment. It was matched against the literal text
/// "Intercept" or "InterceptAttribute", so any other spelling produced no interception at all — no
/// wrapper, no diagnostic, a green build, and a cross-cutting concern that simply stopped running.
/// Interceptors are audit, authorisation, retry and metrics, which is the worst set of things to
/// silently not run.
/// </summary>
public partial class InterceptAttributeSpellingTests
{
    [VerifyTest]
    [Row("[Intercept(typeof(LoggingInterceptor))]")]
    [Row("[InterceptAttribute(typeof(LoggingInterceptor))]")]
    [Row("[DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [Row("[DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]")]
    [Row("[global::DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [Row(
        "[global::DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]"
    )]
    [Row("[Wrap(typeof(LoggingInterceptor))]")]
    public void EverySpelling_GeneratesTheWrapper(string attribute)
    {
        var result = Run(attribute).AssertNoErrors();

        Verify(string.Join(", ", result.GeneratedSources.Keys).Contains("Thing_Intercepted"));
    }

    [VerifyTest]
    [Row("[Intercept(typeof(LoggingInterceptor))]")]
    [Row("[DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [Row(
        "[global::DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]"
    )]
    [Row("[Wrap(typeof(LoggingInterceptor))]")]
    public void EverySpelling_AppliesTheInterception(string attribute)
    {
        var interceptors = Run(attribute).SourceContaining("Interceptors");

        Verify(interceptors.Contains("Thing_Intercepted"));
    }

    private static GeneratorResult Run(string attribute) =>
        GeneratorTestHarness.Run(
            $$"""
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;
            using Wrap = DependencyModules.Runtime.Attributes.InterceptAttribute;

            namespace TestNamespace;

            public interface IThing {
                string Read(string key);
            }

            [SingletonService]
            public class LoggingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
            }

            [SingletonService]
            {{attribute}}
            public class Thing : IThing {
                public string Read(string key) => key;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );
}

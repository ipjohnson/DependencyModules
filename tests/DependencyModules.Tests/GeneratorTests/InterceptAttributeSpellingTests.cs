using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

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
public class InterceptAttributeSpellingTests {

    [Theory]
    [InlineData("[Intercept(typeof(LoggingInterceptor))]")]
    [InlineData("[InterceptAttribute(typeof(LoggingInterceptor))]")]
    [InlineData("[DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [InlineData("[DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]")]
    [InlineData("[global::DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [InlineData("[global::DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]")]
    [InlineData("[Wrap(typeof(LoggingInterceptor))]")]
    public void EverySpelling_GeneratesTheWrapper(string attribute) {
        var result = Run(attribute).AssertNoErrors();

        Assert.Contains("Thing_Intercepted", string.Join(", ", result.GeneratedSources.Keys));
    }

    [Theory]
    [InlineData("[Intercept(typeof(LoggingInterceptor))]")]
    [InlineData("[DependencyModules.Runtime.Attributes.Intercept(typeof(LoggingInterceptor))]")]
    [InlineData("[global::DependencyModules.Runtime.Attributes.InterceptAttribute(typeof(LoggingInterceptor))]")]
    [InlineData("[Wrap(typeof(LoggingInterceptor))]")]
    public void EverySpelling_AppliesTheInterception(string attribute) {
        var interceptors = Run(attribute).SourceContaining("Interceptors");

        Assert.Contains("Thing_Intercepted", interceptors);
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
              """);
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Interception under <c>DependencyModules_GenerateFactories</c>.
///
/// The property emits a <c>provider =&gt; new Impl(...)</c> factory in place of
/// <c>typeof(Impl)</c>, which is what removes the container's reflection and what the AOT guidance
/// recommends turning on. It also erased the implementation type from the descriptor — and the
/// per-implementation interception filter works by asking a descriptor what implementation it was
/// built from. With no answer, the filter never engaged and interception went back to wrapping
/// every registration of the service type: an unmarked sibling returning inside another class's
/// wrapper, interceptors running once per registration, and realm isolation gone.
///
/// So the property that exists for the published build quietly undid the release's headline fix, in
/// exactly the configuration hardest to test.
///
/// These run each case with the property off and on and require the same answer. That is the
/// property's whole contract — it is meant to change how a service is constructed, not what is
/// registered or what wraps it.
/// </summary>
public class GenerateFactoriesInterceptionTests {

    private const string Interceptor =
        """
        public sealed class CountingInterceptor : IInterceptor {
            public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
        }
        """;

    /// <summary>
    /// The 1.1.0 fix itself: a sibling implementation carrying no [Intercept] is not wrapped.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnmarkedSibling_IsNotWrapped(bool generateFactories) {
        var resolved = Resolve(
            """
            [SingletonService] [Intercept(typeof(CountingInterceptor))]
            public sealed class Loud : IGreeter { public string Greet() => "loud"; }

            [SingletonService]
            public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
            """,
            generateFactories);

        Assert.Equal(["Loud_Intercepted", "Quiet"], resolved);
    }

    /// <summary>
    /// And the same registration is not wrapped twice. Two marked implementations each get their
    /// own wrapper rather than both getting both.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoMarkedImplementations_EachGetTheirOwnWrapper(bool generateFactories) {
        var resolved = Resolve(
            """
            [SingletonService] [Intercept(typeof(CountingInterceptor))]
            public sealed class Loud : IGreeter { public string Greet() => "loud"; }

            [SingletonService] [Intercept(typeof(CountingInterceptor))]
            public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
            """,
            generateFactories);

        Assert.Equal(["Loud_Intercepted", "Quiet_Intercepted"], resolved);
    }

    /// <summary>
    /// A keyed registration is still one registration. Agent 08 measured a keyed service coming back
    /// as another implementation's wrapper.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKeyedSibling_IsNotWrapped(bool generateFactories) {
        var generated = Build(
            """
            [SingletonService(Key = "loud")] [Intercept(typeof(CountingInterceptor))]
            public sealed class Loud : IGreeter { public string Greet() => "loud"; }

            [SingletonService(Key = "quiet")]
            public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }
            """,
            generateFactories);

        var provider = generated.BuildProvider();
        var serviceType = generated.Type("IGreeter");

        var loud = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(serviceType, "loud");
        var quiet = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(serviceType, "quiet");

        Assert.Equal("Loud_Intercepted", loud.GetType().Name);
        Assert.Equal("Quiet", quiet.GetType().Name);
    }

    /// <summary>
    /// The convention route to the same registrations. Convention-produced models are built by a
    /// different path from attribute-produced ones, so the exemption has to reach both.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConventionRegisteredSibling_IsNotWrapped(bool generateFactories) {
        var generated = GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;
              using DependencyModules.Runtime.Conventions;
              using DependencyModules.Runtime.Interception;

              namespace TestNamespace;

              public interface IGreeter { string Greet(); }

              {{Interceptor}}

              [Intercept(typeof(CountingInterceptor))]
              public sealed class Loud : IGreeter { public string Greet() => "loud"; }

              public sealed class Quiet : IGreeter { public string Greet() => "quiet"; }

              [DependencyModule]
              public partial class TestModule : IConventionModule {
                  void IConventionModule.Conventions(IConventionDefinitions conventions) {
                      conventions.RegisterAll<IGreeter>().AsSingleton();
                  }
              }
              """,
            buildProperties: new Dictionary<string, string> {
                ["DependencyModules_GenerateFactories"] = generateFactories ? "true" : "false"
            });

        var provider = generated.BuildProvider();

        var resolved = ((System.Collections.IEnumerable)provider
                .GetService(typeof(IEnumerable<>).MakeGenericType(generated.Type("IGreeter")))!)
            .Cast<object>()
            .Select(greeter => greeter.GetType().Name)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Loud_Intercepted", "Quiet"], resolved);
    }

    private static string[] Resolve(string body, bool generateFactories) {
        var generated = Build(body, generateFactories);
        var provider = generated.BuildProvider();

        return ((System.Collections.IEnumerable)provider
                .GetService(typeof(IEnumerable<>).MakeGenericType(generated.Type("IGreeter")))!)
            .Cast<object>()
            .Select(greeter => greeter.GetType().Name)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();
    }

    private static GeneratedAssembly Build(string body, bool generateFactories) =>
        GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;
              using DependencyModules.Runtime.Interception;

              namespace TestNamespace;

              public interface IGreeter { string Greet(); }

              {{Interceptor}}

              {{body}}

              [DependencyModule]
              public partial class TestModule;
              """,
            buildProperties: new Dictionary<string, string> {
                ["DependencyModules_GenerateFactories"] = generateFactories ? "true" : "false"
            });
}

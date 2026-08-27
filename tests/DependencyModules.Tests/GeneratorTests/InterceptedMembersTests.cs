using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// <c>[Intercept].Members</c> — which kinds of member the interceptors are placed around.
///
/// Covering the whole interface is the right default: an interceptor written for auditing or retry
/// has no way to know which members matter, and leaving one out silently is the failure this library
/// works hardest to avoid. It is the wrong default for an interface carrying properties, where a
/// timing or logging interceptor records a call per read. The report's agent kept metrics readable
/// by keying them on member name and filtering afterwards, which is the workaround this removes.
///
/// A member left out is still forwarded — the wrapper implements the whole interface either way. It
/// just does not run through the chain, which is the same path a member no interceptor can serve
/// already took.
/// </summary>
public class InterceptedMembersTests {

    [Fact]
    public void WithNoKindsNamed_EveryMemberIsIntercepted() {
        var calls = Run("[Intercept(typeof(CountingInterceptor))]");

        Assert.Equal(["Handle", "get_Name"], calls);
    }

    [Fact]
    public void NamingMethods_LeavesPropertiesAlone() {
        var calls = Run(
            "[Intercept(typeof(CountingInterceptor), Members = InterceptedMembers.Methods)]");

        Assert.Equal(["Handle"], calls);
    }

    [Fact]
    public void NamingProperties_LeavesMethodsAlone() {
        var calls = Run(
            "[Intercept(typeof(CountingInterceptor), Members = InterceptedMembers.Properties)]");

        Assert.Equal(["get_Name"], calls);
    }

    [Fact]
    public void KindsCombine() {
        var calls = Run(
            "[Intercept(typeof(CountingInterceptor), " +
            "Members = InterceptedMembers.Methods | InterceptedMembers.Properties)]");

        Assert.Equal(["Handle", "get_Name"], calls);
    }

    /// <summary>
    /// The excluded member still works — it is forwarded rather than dropped, and the wrapper still
    /// implements the interface.
    /// </summary>
    [Fact]
    public void AnExcludedMember_IsStillForwarded() {
        var generated = Build(
            "[Intercept(typeof(CountingInterceptor), Members = InterceptedMembers.Methods)]");

        var service = generated.BuildProvider().GetService(generated.Type("IHandler"))!;

        Assert.Equal("Handler_Intercepted", service.GetType().Name);
        Assert.Equal("named", service.GetType().GetProperty("Name")!.GetValue(service));
    }

    /// <summary>
    /// An interceptor that cannot serve a member's shape is DM0015. An interceptor that was never
    /// asked to cover it is not — reporting that would report the feature.
    /// </summary>
    [Fact]
    public void AnExcludedMember_IsNotReportedAsUnserved() {
        var result = GeneratorTestHarness.Run(
            Source("[Intercept(typeof(SyncOnlyInterceptor), Members = InterceptedMembers.Methods)]",
                interceptor: SyncOnly));

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0015");
    }

    private static string[] Run(string attribute) {
        var generated = Build(attribute);
        var provider = generated.BuildProvider();
        var service = provider.GetService(generated.Type("IHandler"))!;

        service.GetType().GetMethod("Handle")!.Invoke(service, ["x"]);
        _ = service.GetType().GetProperty("Name")!.GetValue(service);

        var interceptor = generated.Type("CountingInterceptor");

        return ((System.Collections.IEnumerable)interceptor.GetField("Calls")!.GetValue(null)!)
            .Cast<string>()
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();
    }

    private static GeneratedAssembly Build(string attribute) =>
        GeneratedAssembly.Create(Source(attribute));

    private const string Counting =
        """
        public sealed class CountingInterceptor : IInterceptor {
            public static readonly System.Collections.Generic.List<string> Calls = new();

            public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                Calls.Add(context.Caller.MemberName);
                return context.Proceed();
            }
        }
        """;

    private const string SyncOnly =
        """
        public sealed class SyncOnlyInterceptor : IInterceptor {
            public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
        }
        """;

    private static string Source(string attribute, string interceptor = Counting) =>
        $$"""
          using DependencyModules.Runtime.Attributes;
          using DependencyModules.Runtime.Interception;

          namespace TestNamespace;

          public interface IHandler {
              string Name { get; }
              string Handle(string input);
          }

          {{interceptor}}

          [SingletonService]
          {{attribute}}
          public class Handler : IHandler {
              public string Name => "named";
              public string Handle(string input) => input;
          }

          [DependencyModule]
          public partial class TestModule;
          """;
}

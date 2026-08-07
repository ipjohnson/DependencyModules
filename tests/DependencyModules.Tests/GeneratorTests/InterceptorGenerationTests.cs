using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Interception verified by compiling, loading and calling the generated wrapper, since the point is
/// what the hooks observe rather than what the emitted text looks like.
/// </summary>
public class InterceptorGenerationTests {

    [Fact]
    public void InterceptedService_ResolvesAsTheGeneratedWrapper() {
        var generated = GeneratedAssembly.Create(Source("int Sync(int a);", "public int Sync(int a) => a;"));

        var resolved = generated.ResolveRequired("IWork");

        Assert.EndsWith("_Intercepted", resolved.GetType().Name);
    }

    [Fact]
    public void SyncMethod_ReturnsTheInnerValueAndReportsBothHooks() {
        var generated = GeneratedAssembly.Create(Source("int Sync(int a);", "public int Sync(int a) => a * 2;"));

        var work = generated.ResolveRequired("IWork");

        Assert.Equal(10, Invoke(work, "Sync", 5));
        Assert.Equal(["enter Sync", "exit Sync"], Log(generated));
    }

    [Fact]
    public void VoidMethod_ReportsBothHooks() {
        var generated = GeneratedAssembly.Create(Source("void Run();", "public void Run() { }"));

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(["enter Run", "exit Run"], Log(generated));
    }

    /// <summary>
    /// The failure this guards against is reporting completion when the task is handed back rather
    /// than when the work finishes, which makes every measured duration near zero and hides faults.
    /// </summary>
    [Fact]
    public async Task AsyncMethod_ReportsExitAfterTheWorkCompletes() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.Task<int> Compute(int a);",
            "public async System.Threading.Tasks.Task<int> Compute(int a) { await System.Threading.Tasks.Task.Delay(20); return a * 2; }"));

        var task = (Task<int>)Invoke(generated.ResolveRequired("IWork"), "Compute", 21)!;

        Assert.DoesNotContain("exit Compute", Log(generated));

        var result = await task;

        Assert.Equal(42, result);
        Assert.Contains("exit Compute", Log(generated));
    }

    [Fact]
    public async Task AsyncVoidMethod_ReportsBothHooks() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.Task Run();",
            "public async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(5); }"));

        await (Task)Invoke(generated.ResolveRequired("IWork"), "Run")!;

        Assert.Equal(["enter Run", "exit Run"], Log(generated));
    }

    [Fact]
    public async Task ValueTaskMethod_ReportsBothHooks() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.ValueTask<string> Fetch();",
            "public async System.Threading.Tasks.ValueTask<string> Fetch() { await System.Threading.Tasks.Task.Delay(5); return \"done\"; }"));

        var result = await (ValueTask<string>)Invoke(generated.ResolveRequired("IWork"), "Fetch")!;

        Assert.Equal("done", result);
        Assert.Equal(["enter Fetch", "exit Fetch"], Log(generated));
    }

    [Fact]
    public void ThrowingMethod_ReportsTheErrorAndRethrows() {
        var generated = GeneratedAssembly.Create(Source(
            "void Run();",
            "public void Run() => throw new System.InvalidOperationException(\"boom\");"));

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => Invoke(generated.ResolveRequired("IWork"), "Run"));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("error Run", Log(generated));
        Assert.DoesNotContain("exit Run", Log(generated));
    }

    [Fact]
    public void SeveralInterceptors_EnterInOrderAndExitInReverse() {
        var generated = GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;
              using DependencyModules.Runtime.Interception;

              namespace TestNamespace;

              public static class Recorder {
                  public static readonly System.Collections.Generic.List<string> Entries = new();
              }

              public class FirstInterceptor : IInterceptor {
                  public void OnEnter(in InvocationContext c) => Recorder.Entries.Add("enter first");
                  public void OnExit(in InvocationContext c) => Recorder.Entries.Add("exit first");
              }

              public class SecondInterceptor : IInterceptor {
                  public void OnEnter(in InvocationContext c) => Recorder.Entries.Add("enter second");
                  public void OnExit(in InvocationContext c) => Recorder.Entries.Add("exit second");
              }

              public interface IWork { void Run(); }

              [SingletonService]
              [Intercept(typeof(FirstInterceptor), typeof(SecondInterceptor))]
              public class Work : IWork { public void Run() { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(
            ["enter first", "enter second", "exit second", "exit first"],
            (IEnumerable<string>)generated.Type("Recorder").GetField("Entries")!.GetValue(null)!);
    }

    [Fact]
    public void InterceptedContext_CarriesTheServiceTypeAndMember() {
        var generated = GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;
              using DependencyModules.Runtime.Interception;

              namespace TestNamespace;

              public static class Recorder {
                  public static readonly System.Collections.Generic.List<string> Entries = new();
              }

              public class ContextInterceptor : IInterceptor {
                  public void OnEnter(in InvocationContext c) => Recorder.Entries.Add(c.ToString());
              }

              public interface IWork { void Run(); }

              [SingletonService]
              [Intercept(typeof(ContextInterceptor))]
              public class Work : IWork { public void Run() { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(["IWork.Run"], (IEnumerable<string>)generated.Type("Recorder").GetField("Entries")!.GetValue(null)!);
    }

    [Fact]
    public void ServiceWithNoInterface_ReportsDM0008() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;

            namespace TestNamespace;

            public class NoopInterceptor : IInterceptor { }

            [SingletonService]
            [Intercept(typeof(NoopInterceptor))]
            public class Standalone { public void Run() { } }

            [DependencyModule]
            public partial class TestModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("interface", diagnostic.GetMessage());
    }

    [Fact]
    public void ServiceWithSeveralInterfaces_ReportsDM0008() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;

            namespace TestNamespace;

            public class NoopInterceptor : IInterceptor { }

            public interface IOne { void Run(); }
            public interface ITwo { void Walk(); }

            [SingletonService]
            [Intercept(typeof(NoopInterceptor))]
            public class Both : IOne, ITwo { public void Run() { } public void Walk() { } }

            [DependencyModule]
            public partial class TestModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Assert.Contains("Service", diagnostic.GetMessage());
    }

    private static IReadOnlyList<string> Log(GeneratedAssembly generated) =>
        (IReadOnlyList<string>)generated.Type("Recorder").GetField("Entries")!.GetValue(null)!;

    private static object? Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method)!.Invoke(target, arguments);

    private static string Source(string interfaceMember, string implementation) =>
        $$"""
          using DependencyModules.Runtime.Attributes;
          using DependencyModules.Runtime.Interception;

          namespace TestNamespace;

          public static class Recorder {
              public static readonly System.Collections.Generic.List<string> Entries = new();
          }

          public class TracingInterceptor : IInterceptor {
              public void OnEnter(in InvocationContext c) => Recorder.Entries.Add($"enter {c.MemberName}");
              public void OnExit(in InvocationContext c) => Recorder.Entries.Add($"exit {c.MemberName}");
              public void OnError(in InvocationContext c, System.Exception e) => Recorder.Entries.Add($"error {c.MemberName}");
          }

          public interface IWork { {{interfaceMember}} }

          [SingletonService]
          [Intercept(typeof(TracingInterceptor))]
          public class Work : IWork { {{implementation}} }

          [DependencyModule]
          public partial class TestModule;
          """;
}

using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Interception verified by compiling, loading and calling the generated wrapper, since the point is
/// what an interceptor observes and can change rather than what the emitted text looks like.
/// </summary>
public class InterceptorGenerationTests {

    [Fact]
    public void InterceptedService_ResolvesAsTheGeneratedWrapper() {
        var generated = GeneratedAssembly.Create(Source("int Sync(int a);", "public int Sync(int a) => a;"));

        var resolved = generated.ResolveRequired("IWork");

        Assert.EndsWith("_Intercepted", resolved.GetType().Name);
    }

    [Fact]
    public void SyncMethod_ReturnsTheInnerValueThroughThePipeline() {
        var generated = GeneratedAssembly.Create(Source("int Sync(int a);", "public int Sync(int a) => a * 2;"));

        var work = generated.ResolveRequired("IWork");

        Assert.Equal(10, Invoke(work, "Sync", 5));
        Assert.Equal(["enter Sync", "exit Sync"], Log(generated));
    }

    [Fact]
    public void VoidMethod_RunsThroughThePipeline() {
        var generated = GeneratedAssembly.Create(Source("void Run();", "public void Run() { }"));

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(["enter Run", "exit Run"], Log(generated));
    }

    /// <summary>
    /// The failure this guards against is reporting completion when the task is handed back rather
    /// than when the work finishes, which makes every measured duration near zero and hides faults.
    /// An interceptor awaits inside its own method body, so what follows the await runs when the
    /// call has actually finished.
    /// </summary>
    [Fact]
    public async Task AsyncMethod_ExitsAfterTheWorkCompletes() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.Task<int> Compute(int a);",
            "public async System.Threading.Tasks.Task<int> Compute(int a) { await System.Threading.Tasks.Task.Delay(20); return a * 2; }"));

        var task = (Task<int>)Invoke(generated.ResolveRequired("IWork"), "Compute", 21)!;

        Assert.DoesNotContain("exit Compute", Log(generated));

        var result = await task;

        Assert.Equal(42, result);
        Assert.Equal(["enter Compute", "exit Compute"], Log(generated));
    }

    [Fact]
    public async Task AsyncVoidMethod_RunsThroughThePipeline() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.Task Run();",
            "public async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(5); }"));

        await (Task)Invoke(generated.ResolveRequired("IWork"), "Run")!;

        Assert.Equal(["enter Run", "exit Run"], Log(generated));
    }

    [Fact]
    public async Task ValueTaskMethod_ReturnsTheInnerValue() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.ValueTask<string> Fetch();",
            "public async System.Threading.Tasks.ValueTask<string> Fetch() { await System.Threading.Tasks.Task.Delay(5); return \"done\"; }"));

        var result = await (ValueTask<string>)Invoke(generated.ResolveRequired("IWork"), "Fetch")!;

        Assert.Equal("done", result);
        Assert.Equal(["enter Fetch", "exit Fetch"], Log(generated));
    }

    [Fact]
    public async Task ValueTaskWithNoResult_RunsThroughThePipeline() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.ValueTask Save();",
            "public async System.Threading.Tasks.ValueTask Save() { await System.Threading.Tasks.Task.Delay(5); }"));

        await (ValueTask)Invoke(generated.ResolveRequired("IWork"), "Save")!;

        Assert.Equal(["enter Save", "exit Save"], Log(generated));
    }

    /// <summary>
    /// A stream hands its enumerable back immediately, so wrapping it as an ordinary value would
    /// time the construction of the iterator. The interceptor enumerates it instead, and sees each
    /// item as it is produced.
    /// </summary>
    [Fact]
    public async Task AsyncEnumerableMethod_ObservesEachItem() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Collections.Generic.IAsyncEnumerable<int> Stream(int count);",
            """
            public async System.Collections.Generic.IAsyncEnumerable<int> Stream(int count) {
                for (var i = 0; i < count; i++) { await System.Threading.Tasks.Task.Yield(); yield return i; }
            }
            """));

        var items = new List<int>();

        await foreach (var item in (IAsyncEnumerable<int>)Invoke(generated.ResolveRequired("IWork"), "Stream", 3)!) {
            items.Add(item);
        }

        Assert.Equal([0, 1, 2], items);
        Assert.Equal(["enter Stream", "item 0", "item 1", "item 2", "exit Stream"], Log(generated));
    }

    [Fact]
    public void GenericMethod_ForwardsWithItsConstraints() {
        var generated = GeneratedAssembly.Create(Source(
            "T Pick<T>(T item) where T : class;",
            "public T Pick<T>(T item) where T : class => item;"));

        var work = generated.ResolveRequired("IWork");
        var method = work.GetType().GetMethod("Pick")!.MakeGenericMethod(typeof(string));

        Assert.Equal("value", method.Invoke(work, ["value"]));
        Assert.Equal(["enter Pick", "exit Pick"], Log(generated));
    }

    /// <summary>
    /// Accessors are reported the way the CLR names them, so a getter and a setter are told apart
    /// and the name matches what appears in a stack trace.
    /// </summary>
    [Fact]
    public void Property_RoutesBothAccessorsAndReportsThemByTheirClrNames() {
        var generated = GeneratedAssembly.Create(Source(
            "string Name { get; set; }",
            "public string Name { get; set; } = \"initial\";"));

        var work = generated.ResolveRequired("IWork");
        var property = work.GetType().GetProperty("Name")!;

        property.SetValue(work, "assigned");

        Assert.Equal("assigned", property.GetValue(work));
        Assert.Equal(["enter set_Name", "exit set_Name", "enter get_Name", "exit get_Name"], Log(generated));
    }

    [Fact]
    public void ReadOnlyProperty_DeclaresNoSetter() {
        var generated = GeneratedAssembly.Create(Source("int Count { get; }", "public int Count => 7;"));

        var work = generated.ResolveRequired("IWork");
        var property = work.GetType().GetProperty("Count")!;

        Assert.Equal(7, property.GetValue(work));
        Assert.Null(property.SetMethod);
        Assert.Equal(["enter get_Count", "exit get_Count"], Log(generated));
    }

    /// <summary>
    /// An accessor cannot be async whatever its type, so a property of task type takes the sync path
    /// and hands the task itself to the interceptor.
    /// </summary>
    [Fact]
    public async Task PropertyReturningATask_TakesTheSyncPath() {
        var generated = GeneratedAssembly.Create(Source(
            "System.Threading.Tasks.Task<int> Pending { get; }",
            "public System.Threading.Tasks.Task<int> Pending => System.Threading.Tasks.Task.FromResult(3);"));

        var work = generated.ResolveRequired("IWork");
        var pending = (Task<int>)work.GetType().GetProperty("Pending")!.GetValue(work)!;

        Assert.Equal(3, await pending);
        Assert.Equal(["enter get_Pending", "exit get_Pending"], Log(generated));
    }

    [Fact]
    public void Indexer_ForwardsItsIndicesAndAssignedValue() {
        var generated = GeneratedAssembly.Create(Source(
            "int this[int row, int column] { get; set; }",
            """
            private readonly System.Collections.Generic.Dictionary<string, int> _cells = new();
            public int this[int row, int column] {
                get => _cells.TryGetValue($"{row},{column}", out var value) ? value : -1;
                set => _cells[$"{row},{column}"] = value;
            }
            """));

        var work = generated.ResolveRequired("IWork");
        var indexer = work.GetType().GetProperty("Item")!;

        indexer.SetValue(work, 42, [2, 3]);

        Assert.Equal(42, indexer.GetValue(work, [2, 3]));
        Assert.Equal(-1, indexer.GetValue(work, [9, 9]));
        Assert.Equal(
            ["enter set_Item", "exit set_Item", "enter get_Item", "exit get_Item", "enter get_Item", "exit get_Item"],
            Log(generated));
    }

    [Fact]
    public void IndexerSetter_ExposesItsIndicesAndValueAsArguments() {
        var generated = GeneratedAssembly.Create(Source(
            "int this[int row] { get; set; }",
            "public int this[int row] { get => row; set { } }",
            """
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    var arguments = context.Arguments;

                    for (var i = 0; i < arguments.Count; i++) {
                        Recorder.Entries.Add($"{arguments.NameAt(i)}={arguments[i]}");
                    }

                    return context.Proceed();
                }
            }
            """));

        var work = generated.ResolveRequired("IWork");

        work.GetType().GetProperty("Item")!.SetValue(work, 8, [1]);

        Assert.Equal(["row=1", "value=8"], Log(generated));
    }

    [Fact]
    public void Event_RoutesAddAndRemove() {
        var generated = GeneratedAssembly.Create(Source(
            "event System.EventHandler Changed;",
            "public event System.EventHandler Changed { add { } remove { } }"));

        var work = generated.ResolveRequired("IWork");
        var changed = work.GetType().GetEvent("Changed")!;
        EventHandler handler = (_, _) => { };

        changed.AddEventHandler(work, handler);
        changed.RemoveEventHandler(work, handler);

        Assert.Equal(
            ["enter add_Changed", "exit add_Changed", "enter remove_Changed", "exit remove_Changed"],
            Log(generated));
    }

    [Fact]
    public void ThrowingMethod_PropagatesThroughThePipeline() {
        var generated = GeneratedAssembly.Create(Source(
            "void Run();",
            "public void Run() => throw new System.InvalidOperationException(\"boom\");"));

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => Invoke(generated.ResolveRequired("IWork"), "Run"));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(["enter Run", "exit Run"], Log(generated));
    }

    [Fact]
    public void SeveralInterceptors_NestInDeclarationOrder() {
        var generated = GeneratedAssembly.Create(
            $$"""
              {{Preamble}}

              {{Tracing("First", "first")}}

              {{Tracing("Second", "second")}}

              public interface IWork { void Run(); }

              [SingletonService]
              [Intercept(typeof(FirstInterceptor), typeof(SecondInterceptor))]
              public class Work : IWork { public void Run() { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(
            ["enter first Run", "enter second Run", "exit second Run", "exit first Run"],
            Log(generated));
    }

    [Fact]
    public void Caller_CarriesTheServiceTypeAndMember() {
        var generated = GeneratedAssembly.Create(Source(
            "void Run();",
            "public void Run() { }",
            """
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Recorder.Entries.Add(context.Caller.ToString());
                    Recorder.Entries.Add(context.Caller.ServiceType.Name);
                    return context.Proceed();
                }
            }
            """));

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Assert.Equal(["IWork.Run", "IWork"], Log(generated));
    }

    /// <summary>
    /// Arguments live as typed fields on the state, which is where the inner call reads them from,
    /// so writing one replaces the value the implementation receives.
    /// </summary>
    [Fact]
    public void Arguments_AreReadableByNameAndReplaceable() {
        var generated = GeneratedAssembly.Create(Source(
            "int Sync(int a, string b);",
            "public int Sync(int a, string b) { Recorder.Entries.Add($\"inner {a} {b}\"); return a; }",
            """
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    var arguments = context.Arguments;

                    for (var i = 0; i < arguments.Count; i++) {
                        Recorder.Entries.Add($"{arguments.NameAt(i)}={arguments[i]}");
                    }

                    arguments[0] = 99;
                    return context.Proceed();
                }
            }
            """));

        var result = Invoke(generated.ResolveRequired("IWork"), "Sync", 5, "text");

        Assert.Equal(99, result);
        Assert.Equal(["a=5", "b=text", "inner 99 text"], Log(generated));
    }

    /// <summary>
    /// The stage index is carried on the context rather than mutated on the state, so proceeding a
    /// second time re-enters the same next stage rather than walking past it.
    /// </summary>
    [Fact]
    public void ProceedingTwice_CallsTheImplementationTwice() {
        var generated = GeneratedAssembly.Create(Source(
            "int Sync(int a);",
            "public int Sync(int a) { Recorder.Entries.Add(\"inner\"); return a; }",
            """
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    context.Proceed();
                    return context.Proceed();
                }
            }
            """));

        var result = Invoke(generated.ResolveRequired("IWork"), "Sync", 5);

        Assert.Equal(5, result);
        Assert.Equal(["inner", "inner"], Log(generated));
    }

    [Fact]
    public void NotProceeding_SkipsTheImplementation() {
        var generated = GeneratedAssembly.Create(Source(
            "int Sync(int a);",
            "public int Sync(int a) { Recorder.Entries.Add(\"inner\"); return a; }",
            """
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) => default!;
            }
            """));

        Assert.Equal(0, Invoke(generated.ResolveRequired("IWork"), "Sync", 5));
        Assert.Empty(Log(generated));
    }

    /// <summary>
    /// Registering every interceptor under the shared IInterceptor interface made all of them
    /// visible to every wrapper: two services with different interceptors each ran both. The
    /// wrapper holds its interceptors as typed fields now, so a wrapper can only reach its own.
    /// </summary>
    [Fact]
    public void TwoServices_DoNotCrossApplyEachOthersInterceptors() {
        var generated = GeneratedAssembly.Create(
            $$"""
              {{Preamble}}

              {{Tracing("Alpha", "alpha")}}

              {{Tracing("Beta", "beta")}}

              public interface IAlpha { void Run(); }
              public interface IBeta { void Run(); }

              [SingletonService]
              [Intercept(typeof(AlphaInterceptor))]
              public class Alpha : IAlpha { public void Run() { } }

              [SingletonService]
              [Intercept(typeof(BetaInterceptor))]
              public class Beta : IBeta { public void Run() { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        var provider = generated.BuildProvider();

        Invoke(provider.GetService(generated.Type("IAlpha"))!, "Run");

        Assert.Equal(["enter alpha Run", "exit alpha Run"], Log(generated));
    }

    [Fact]
    public void InterceptorMissingTheAsyncInterface_ReportsDM0009() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              public class SyncOnlyInterceptor : IInterceptor {
                  public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
              }

              public interface IWork { System.Threading.Tasks.Task Run(); }

              [SingletonService]
              [Intercept(typeof(SyncOnlyInterceptor))]
              public class Work : IWork {
                  public System.Threading.Tasks.Task Run() => System.Threading.Tasks.Task.CompletedTask;
              }

              [DependencyModule]
              public partial class TestModule;
              """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("IAsyncInterceptor", diagnostic.GetMessage());
        Assert.Contains("Run", diagnostic.GetMessage());
    }

    [Fact]
    public void InterceptorImplementingNoInterceptorInterface_ReportsDM0009() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              public class NotAnInterceptor { }

              public interface IWork { void Run(); }

              [SingletonService]
              [Intercept(typeof(NotAnInterceptor))]
              public class Work : IWork { public void Run() { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Contains("implements none of", diagnostic.GetMessage());
    }

    [Fact]
    public void RefParameter_ReportsDM0008() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              {{Tracing("Tracing", "tracing")}}

              public interface IWork { void Run(ref int a); }

              [SingletonService]
              [Intercept(typeof(TracingInterceptor))]
              public class Work : IWork { public void Run(ref int a) { } }

              [DependencyModule]
              public partial class TestModule;
              """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Assert.Contains("by ref", diagnostic.GetMessage());
    }

    [Fact]
    public void ServiceWithNoInterface_ReportsDM0008() {
        var result = GeneratorTestHarness.Run(
            $$"""
              {{Preamble}}

              {{Tracing("Tracing", "tracing")}}

              [SingletonService]
              [Intercept(typeof(TracingInterceptor))]
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
            $$"""
              {{Preamble}}

              {{Tracing("Tracing", "tracing")}}

              public interface IOne { void Run(); }
              public interface ITwo { void Walk(); }

              [SingletonService]
              [Intercept(typeof(TracingInterceptor))]
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

    private const string Preamble =
        """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interception;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace TestNamespace;

        public static class Recorder {
            public static readonly List<string> Entries = new();
        }
        """;

    /// <summary>
    /// An interceptor covering all three interfaces, so a test only has to choose the member shape
    /// it is exercising rather than a matching interceptor as well.
    /// </summary>
    private static string Tracing(string prefix, string label) =>
        $$"""
          public class {{prefix}}Interceptor : IInterceptor, IAsyncInterceptor, IAsyncEnumerableInterceptor {
              public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                  Recorder.Entries.Add($"enter {{label}} {context.Caller.MemberName}");

                  try {
                      return context.Proceed();
                  } finally {
                      Recorder.Entries.Add($"exit {{label}} {context.Caller.MemberName}");
                  }
              }

              public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
                  Recorder.Entries.Add($"enter {{label}} {context.Caller.MemberName}");

                  try {
                      return await context.ProceedAsync();
                  } finally {
                      Recorder.Entries.Add($"exit {{label}} {context.Caller.MemberName}");
                  }
              }

              public async IAsyncEnumerable<TItem> InterceptStream<TItem>(StreamInvocationContext<TItem> context) {
                  Recorder.Entries.Add($"enter {{label}} {context.Caller.MemberName}");

                  await foreach (var item in context.Proceed()) {
                      Recorder.Entries.Add($"item {item}");

                      yield return item;
                  }

                  Recorder.Entries.Add($"exit {{label}} {context.Caller.MemberName}");
              }
          }
          """;

    /// <summary>
    /// The default interceptor logs without a label, so the single-interceptor tests read as
    /// "enter Sync" rather than repeating which interceptor produced the entry.
    /// </summary>
    private static readonly string DefaultInterceptor = Tracing("Tracing", "").Replace(" {context.Caller", "{context.Caller");

    private static string Source(string interfaceMember, string implementation, string? interceptor = null) =>
        $$"""
          {{Preamble}}

          {{interceptor ?? DefaultInterceptor}}

          public interface IWork { {{interfaceMember}} }

          [SingletonService]
          [Intercept(typeof(TracingInterceptor))]
          public class Work : IWork { {{implementation}} }

          [DependencyModule]
          public partial class TestModule;
          """;
}

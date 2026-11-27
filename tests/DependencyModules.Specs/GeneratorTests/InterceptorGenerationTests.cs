using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Interception verified by compiling, loading and calling the generated wrapper, since the point is
/// what an interceptor observes and can change rather than what the emitted text looks like.
/// </summary>
public partial class InterceptorGenerationTests
{
    [VerifyTest]
    public void InterceptedService_ResolvesAsTheGeneratedWrapper()
    {
        var generated = GeneratedAssembly.Create(
            Source("int Sync(int a);", "public int Sync(int a) => a;")
        );

        var resolved = generated.ResolveRequired("IWork");

        Verify(resolved.GetType().Name.EndsWith("_Intercepted"));
    }

    [VerifyTest]
    public void SyncMethod_ReturnsTheInnerValueThroughThePipeline()
    {
        var generated = GeneratedAssembly.Create(
            Source("int Sync(int a);", "public int Sync(int a) => a * 2;")
        );

        var work = generated.ResolveRequired("IWork");

        Verify(Equals(Invoke(work, "Sync", 5), 10));
        Verify(Log(generated).SequenceEqual(["enter Sync", "exit Sync"]));
    }

    [VerifyTest]
    public void VoidMethod_RunsThroughThePipeline()
    {
        var generated = GeneratedAssembly.Create(Source("void Run();", "public void Run() { }"));

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Verify(Log(generated).SequenceEqual(["enter Run", "exit Run"]));
    }

    /// <summary>
    /// The failure this guards against is reporting completion when the task is handed back rather
    /// than when the work finishes, which makes every measured duration near zero and hides faults.
    /// An interceptor awaits inside its own method body, so what follows the await runs when the
    /// call has actually finished.
    /// </summary>
    [VerifyTest]
    public async Task AsyncMethod_ExitsAfterTheWorkCompletes()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Threading.Tasks.Task<int> Compute(int a);",
                "public async System.Threading.Tasks.Task<int> Compute(int a) { await Recorder.Gate.Task; return a * 2; }"
            )
        );

        var task = (Task<int>)Invoke(generated.ResolveRequired("IWork"), "Compute", 21)!;

        // The work is suspended at a gate nothing has released, so this cannot have happened yet.
        Verify(!Log(generated).Contains("exit Compute"));

        Gate(generated).SetResult();

        var result = await task;

        Verify(result == 42);
        Verify(Log(generated).SequenceEqual(["enter Compute", "exit Compute"]));
    }

    [VerifyTest]
    public async Task AsyncVoidMethod_RunsThroughThePipeline()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Threading.Tasks.Task Run();",
                "public async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(5); }"
            )
        );

        await (Task)Invoke(generated.ResolveRequired("IWork"), "Run")!;

        Verify(Log(generated).SequenceEqual(["enter Run", "exit Run"]));
    }

    [VerifyTest]
    public async Task ValueTaskMethod_ReturnsTheInnerValue()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Threading.Tasks.ValueTask<string> Fetch();",
                "public async System.Threading.Tasks.ValueTask<string> Fetch() { await System.Threading.Tasks.Task.Delay(5); return \"done\"; }"
            )
        );

        var result = await (ValueTask<string>)Invoke(generated.ResolveRequired("IWork"), "Fetch")!;

        Verify(result == "done");
        Verify(Log(generated).SequenceEqual(["enter Fetch", "exit Fetch"]));
    }

    [VerifyTest]
    public async Task ValueTaskWithNoResult_RunsThroughThePipeline()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Threading.Tasks.ValueTask Save();",
                "public async System.Threading.Tasks.ValueTask Save() { await System.Threading.Tasks.Task.Delay(5); }"
            )
        );

        await (ValueTask)Invoke(generated.ResolveRequired("IWork"), "Save")!;

        Verify(Log(generated).SequenceEqual(["enter Save", "exit Save"]));
    }

    /// <summary>
    /// A stream hands its enumerable back immediately, so wrapping it as an ordinary value would
    /// time the construction of the iterator. The interceptor enumerates it instead, and sees each
    /// item as it is produced.
    /// </summary>
    [VerifyTest]
    public async Task AsyncEnumerableMethod_ObservesEachItem()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Collections.Generic.IAsyncEnumerable<int> Stream(int count);",
                """
                public async System.Collections.Generic.IAsyncEnumerable<int> Stream(int count) {
                    for (var i = 0; i < count; i++) { await System.Threading.Tasks.Task.Yield(); yield return i; }
                }
                """
            )
        );

        var items = new List<int>();

        await foreach (
            var item in (IAsyncEnumerable<int>)
                Invoke(generated.ResolveRequired("IWork"), "Stream", 3)!
        )
        {
            items.Add(item);
        }

        Verify(items.SequenceEqual([0, 1, 2]));
        Verify(
            Log(generated)
                .SequenceEqual(["enter Stream", "item 0", "item 1", "item 2", "exit Stream"])
        );
    }

    [VerifyTest]
    public void GenericMethod_ForwardsWithItsConstraints()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "T Pick<T>(T item) where T : class;",
                "public T Pick<T>(T item) where T : class => item;"
            )
        );

        var work = generated.ResolveRequired("IWork");
        var method = work.GetType().GetMethod("Pick")!.MakeGenericMethod(typeof(string));

        Verify(Equals(method.Invoke(work, ["value"]), "value"));
        Verify(Log(generated).SequenceEqual(["enter Pick", "exit Pick"]));
    }

    /// <summary>
    /// Accessors are reported the way the CLR names them, so a getter and a setter are told apart
    /// and the name matches what appears in a stack trace.
    /// </summary>
    [VerifyTest]
    public void Property_RoutesBothAccessorsAndReportsThemByTheirClrNames()
    {
        var generated = GeneratedAssembly.Create(
            Source("string Name { get; set; }", "public string Name { get; set; } = \"initial\";")
        );

        var work = generated.ResolveRequired("IWork");
        var property = work.GetType().GetProperty("Name")!;

        property.SetValue(work, "assigned");

        Verify(Equals(property.GetValue(work), "assigned"));
        Verify(
            Log(generated)
                .SequenceEqual([
                    "enter set_Name",
                    "exit set_Name",
                    "enter get_Name",
                    "exit get_Name",
                ])
        );
    }

    [VerifyTest]
    public void ReadOnlyProperty_DeclaresNoSetter()
    {
        var generated = GeneratedAssembly.Create(
            Source("int Count { get; }", "public int Count => 7;")
        );

        var work = generated.ResolveRequired("IWork");
        var property = work.GetType().GetProperty("Count")!;

        Verify(Equals(property.GetValue(work), 7));
        Verify(property.SetMethod is null);
        Verify(Log(generated).SequenceEqual(["enter get_Count", "exit get_Count"]));
    }

    /// <summary>
    /// An accessor cannot be async whatever its type, so a property of task type takes the sync path
    /// and hands the task itself to the interceptor.
    /// </summary>
    [VerifyTest]
    public async Task PropertyReturningATask_TakesTheSyncPath()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "System.Threading.Tasks.Task<int> Pending { get; }",
                "public System.Threading.Tasks.Task<int> Pending => System.Threading.Tasks.Task.FromResult(3);"
            )
        );

        var work = generated.ResolveRequired("IWork");
        var pending = (Task<int>)work.GetType().GetProperty("Pending")!.GetValue(work)!;

        Verify(await pending == 3);
        Verify(Log(generated).SequenceEqual(["enter get_Pending", "exit get_Pending"]));
    }

    [VerifyTest]
    public void Indexer_ForwardsItsIndicesAndAssignedValue()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "int this[int row, int column] { get; set; }",
                """
                private readonly System.Collections.Generic.Dictionary<string, int> _cells = new();
                public int this[int row, int column] {
                    get => _cells.TryGetValue($"{row},{column}", out var value) ? value : -1;
                    set => _cells[$"{row},{column}"] = value;
                }
                """
            )
        );

        var work = generated.ResolveRequired("IWork");
        var indexer = work.GetType().GetProperty("Item")!;

        indexer.SetValue(work, 42, [2, 3]);

        Verify(Equals(indexer.GetValue(work, [2, 3]), 42));
        Verify(Equals(indexer.GetValue(work, [9, 9]), -1));
        Verify(
            Log(generated)
                .SequenceEqual([
                    "enter set_Item",
                    "exit set_Item",
                    "enter get_Item",
                    "exit get_Item",
                    "enter get_Item",
                    "exit get_Item",
                ])
        );
    }

    [VerifyTest]
    public void IndexerSetter_ExposesItsIndicesAndValueAsArguments()
    {
        var generated = GeneratedAssembly.Create(
            Source(
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
                """
            )
        );

        var work = generated.ResolveRequired("IWork");

        work.GetType().GetProperty("Item")!.SetValue(work, 8, [1]);

        Verify(Log(generated).SequenceEqual(["row=1", "value=8"]));
    }

    [VerifyTest]
    public void Event_RoutesAddAndRemove()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "event System.EventHandler Changed;",
                "public event System.EventHandler Changed { add { } remove { } }"
            )
        );

        var work = generated.ResolveRequired("IWork");
        var changed = work.GetType().GetEvent("Changed")!;
        EventHandler handler = (_, _) => { };

        changed.AddEventHandler(work, handler);
        changed.RemoveEventHandler(work, handler);

        Verify(
            Log(generated)
                .SequenceEqual([
                    "enter add_Changed",
                    "exit add_Changed",
                    "enter remove_Changed",
                    "exit remove_Changed",
                ])
        );
    }

    [VerifyTest]
    public void ThrowingMethod_PropagatesThroughThePipeline()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "void Run();",
                "public void Run() => throw new System.InvalidOperationException(\"boom\");"
            )
        );

        var exception = ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            Invoke(generated.ResolveRequired("IWork"), "Run")
        );

        Verify(exception.InnerException?.GetType() == typeof(InvalidOperationException));
        Verify(Log(generated).SequenceEqual(["enter Run", "exit Run"]));
    }

    [VerifyTest]
    public void SeveralInterceptors_NestInDeclarationOrder()
    {
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
            """
        );

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Verify(
            Log(generated)
                .SequenceEqual([
                    "enter first Run",
                    "enter second Run",
                    "exit second Run",
                    "exit first Run",
                ])
        );
    }

    [VerifyTest]
    public void Caller_CarriesTheServiceTypeAndMember()
    {
        var generated = GeneratedAssembly.Create(
            Source(
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
                """
            )
        );

        Invoke(generated.ResolveRequired("IWork"), "Run");

        Verify(Log(generated).SequenceEqual(["IWork.Run", "IWork"]));
    }

    /// <summary>
    /// Arguments live as typed fields on the state, which is where the inner call reads them from,
    /// so writing one replaces the value the implementation receives.
    /// </summary>
    [VerifyTest]
    public void Arguments_AreReadableByNameAndReplaceable()
    {
        var generated = GeneratedAssembly.Create(
            Source(
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
                """
            )
        );

        var result = Invoke(generated.ResolveRequired("IWork"), "Sync", 5, "text");

        Verify(Equals(result, 99));
        Verify(Log(generated).SequenceEqual(["a=5", "b=text", "inner 99 text"]));
    }

    /// <summary>
    /// The stage index is carried on the context rather than mutated on the state, so proceeding a
    /// second time re-enters the same next stage rather than walking past it.
    /// </summary>
    [VerifyTest]
    public void ProceedingTwice_CallsTheImplementationTwice()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "int Sync(int a);",
                "public int Sync(int a) { Recorder.Entries.Add(\"inner\"); return a; }",
                """
                public class TracingInterceptor : IInterceptor {
                    public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                        context.Proceed();
                        return context.Proceed();
                    }
                }
                """
            )
        );

        var result = Invoke(generated.ResolveRequired("IWork"), "Sync", 5);

        Verify(Equals(result, 5));
        Verify(Log(generated).SequenceEqual(["inner", "inner"]));
    }

    [VerifyTest]
    public void NotProceeding_SkipsTheImplementation()
    {
        var generated = GeneratedAssembly.Create(
            Source(
                "int Sync(int a);",
                "public int Sync(int a) { Recorder.Entries.Add(\"inner\"); return a; }",
                """
                public class TracingInterceptor : IInterceptor {
                    public TResult Intercept<TResult>(InvocationContext<TResult> context) => default!;
                }
                """
            )
        );

        Verify(Equals(Invoke(generated.ResolveRequired("IWork"), "Sync", 5), 0));
        Verify(Log(generated).Count == 0);
    }

    /// <summary>
    /// Registering every interceptor under the shared IInterceptor interface made all of them
    /// visible to every wrapper: two services with different interceptors each ran both. The
    /// wrapper holds its interceptors as typed fields now, so a wrapper can only reach its own.
    /// </summary>
    [VerifyTest]
    public void TwoServices_DoNotCrossApplyEachOthersInterceptors()
    {
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
            """
        );

        var provider = generated.BuildProvider();

        Invoke(provider.GetService(generated.Type("IAlpha"))!, "Run");

        Verify(Log(generated).SequenceEqual(["enter alpha Run", "exit alpha Run"]));
    }

    /// <summary>
    /// An interface is intercepted as a whole, and one mixing synchronous and asynchronous members
    /// is ordinary. A sync-only interceptor serves the sync members and has nothing to say about the
    /// rest, which are forwarded untouched rather than costing the sync members their interception.
    /// </summary>
    [VerifyTest]
    public async Task SyncOnlyInterceptor_ServesSyncMembersAndPassesAsyncOnesThrough()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            {{Preamble}}

            public class SyncOnlyInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Recorder.Entries.Add($"enter {context.Caller.MemberName}");

                    try {
                        return context.Proceed();
                    } finally {
                        Recorder.Entries.Add($"exit {context.Caller.MemberName}");
                    }
                }
            }

            public interface IWork {
                int Sync(int a);
                Task<int> Async(int a);
            }

            [SingletonService]
            [Intercept(typeof(SyncOnlyInterceptor))]
            public class Work : IWork {
                public int Sync(int a) => a * 2;
                public Task<int> Async(int a) => Task.FromResult(a * 3);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var work = generated.ResolveRequired("IWork");

        Verify(Equals(Invoke(work, "Sync", 5), 10));
        Verify(Log(generated).SequenceEqual(["enter Sync", "exit Sync"]));

        Verify(await (Task<int>)Invoke(work, "Async", 5)! == 15);
        Verify(Log(generated).SequenceEqual(["enter Sync", "exit Sync"]));
    }

    /// <summary>
    /// The mirror of the above, so neither direction is the special case.
    /// </summary>
    [VerifyTest]
    public async Task AsyncOnlyInterceptor_ServesAsyncMembersAndPassesSyncOnesThrough()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            {{Preamble}}

            public class AsyncOnlyInterceptor : IAsyncInterceptor {
                public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
                    Recorder.Entries.Add($"enter {context.Caller.MemberName}");

                    try {
                        return await context.ProceedAsync();
                    } finally {
                        Recorder.Entries.Add($"exit {context.Caller.MemberName}");
                    }
                }
            }

            public interface IWork {
                int Sync(int a);
                Task<int> Async(int a);
            }

            [SingletonService]
            [Intercept(typeof(AsyncOnlyInterceptor))]
            public class Work : IWork {
                public int Sync(int a) => a * 2;
                public Task<int> Async(int a) => Task.FromResult(a * 3);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var work = generated.ResolveRequired("IWork");

        Verify(Equals(Invoke(work, "Sync", 5), 10));
        Verify(Log(generated).Count == 0);

        Verify(await (Task<int>)Invoke(work, "Async", 5)! == 15);
        Verify(Log(generated).SequenceEqual(["enter Async", "exit Async"]));
    }

    /// <summary>
    /// Each member's pipeline is only the interceptors that can serve it, so the stage a proceed
    /// walks to is its position in that pipeline rather than in the attribute.
    /// </summary>
    [VerifyTest]
    public void MixedInterceptors_NestOnlyThoseThatServeTheMember()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            {{Preamble}}

            public class SyncOnlyInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Recorder.Entries.Add("enter sync-only");

                    try {
                        return context.Proceed();
                    } finally {
                        Recorder.Entries.Add("exit sync-only");
                    }
                }
            }

            {{Tracing("Both", "both")}}

            public interface IWork { int Sync(int a); }

            [SingletonService]
            [Intercept(typeof(SyncOnlyInterceptor), typeof(BothInterceptor))]
            public class Work : IWork { public int Sync(int a) => a; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Invoke(generated.ResolveRequired("IWork"), "Sync", 1);

        Verify(
            Log(generated)
                .SequenceEqual([
                    "enter sync-only",
                    "enter both Sync",
                    "exit both Sync",
                    "exit sync-only",
                ])
        );
    }

    /// <summary>
    /// An interceptor that can serve nothing on the service produces no wrapper at all, so the
    /// service resolves as the implementation registered it.
    /// </summary>
    [VerifyTest]
    public void InterceptorThatServesNothing_GeneratesNoWrapper()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            {{Preamble}}

            public class SyncOnlyInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
            }

            public interface IWork { Task<int> Async(int a); }

            [SingletonService]
            [Intercept(typeof(SyncOnlyInterceptor))]
            public class Work : IWork { public Task<int> Async(int a) => Task.FromResult(a); }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var resolved = generated.ResolveRequired("IWork");

        Verify(resolved.GetType().Name == "Work");
    }

    /// <summary>
    /// A generic implementation registers as an open generic. Decoration cannot touch one — it
    /// rewrites the registration into a factory, and the container refuses a factory for an open
    /// generic service type — but interception does not need one: the wrapper is a generated type,
    /// and an open generic implementation type is what the container does accept.
    /// </summary>
    [VerifyTest]
    public void GenericImplementation_IsIntercepted()
    {
        var result = GeneratorTestHarness.Run(
            GenericRepo("public class Repo<T> : IRepo<T> { public void Run() { } }")
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0008"));
        Verify(result.GeneratedSources.Keys.Any(key => key.Contains("Repo_Intercepted")));
    }

    /// <summary>
    /// The wrapper is generic over the same parameters, so the container can register it as an open
    /// generic implementation type and close it per construction.
    /// </summary>
    [VerifyTest]
    public void GenericImplementation_EmitsAGenericWrapper()
    {
        var result = GeneratorTestHarness.Run(
            GenericRepo("public class Repo<T> : IRepo<T> { public void Run() { } }")
        );

        var wrapper = Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("Repo_Intercepted")
        ).Value;

        Verify(wrapper.Contains("class Repo_Intercepted<T>"));

        // It takes the implementation by its own type. Asking for the service would resolve the
        // wrapper itself, which is registered as that service, and recurse.
        Verify(wrapper.Contains("Repo<T> inner"));
    }

    /// <summary>
    /// Registered by swapping the open generic registration rather than through the decorator
    /// factory, which an open generic service type cannot carry. Every type is written unbound: no
    /// `T` is in scope at the registration.
    /// </summary>
    [VerifyTest]
    public void GenericImplementation_RegistersAsAnOpenGenericImplementation()
    {
        var result = GeneratorTestHarness.Run(
            GenericRepo("public class Repo<T> : IRepo<T> { public void Run() { } }")
        );

        var registration = Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("Interceptors")
        ).Value;

        Verify(registration.Contains("InterceptOpenGeneric"));
        Verify(registration.Contains("typeof(global::TestNamespace.IRepo<>)"));
        Verify(registration.Contains("typeof(global::TestNamespace.Repo_Intercepted<>)"));
    }

    /// <summary>
    /// The wrapper repeats the implementation's constraints, without which it could not reference
    /// what it wraps.
    /// </summary>
    [VerifyTest]
    public void ConstrainedGenericImplementation_RepeatsTheConstraints()
    {
        var result = GeneratorTestHarness.Run(
            GenericRepo(
                "public class Repo<T> : IRepo<T> where T : class, IMarker, new() { public void Run() { } }",
                supporting: "public interface IMarker;"
            )
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0008"));

        var wrapper = Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("Repo_Intercepted")
        ).Value;

        Verify(wrapper.Contains("where T : class, global::TestNamespace.IMarker, new()"));
    }

    /// <summary>
    /// struct already guarantees a default constructor, and repeating new() alongside it is CS0451.
    /// Roslyn reports the constructor constraint for a struct-constrained parameter anyway, so the
    /// reader has to drop it rather than pass it through.
    /// </summary>
    [VerifyTest]
    public void StructConstrainedGeneric_DoesNotRepeatTheDefaultConstructor()
    {
        var result = GeneratorTestHarness.Run(
            GenericRepo(
                "public class Repo<T> : IRepo<T> where T : struct { public void Run() { } }"
            )
        );

        var wrapper = Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("Repo_Intercepted")
        ).Value;

        Verify(wrapper.Contains("where T : struct"));
        Verify(!wrapper.Contains("new()"));
    }

    /// <summary>
    /// And the constrained wrapper is not merely well-formed text: it compiles, loads and runs.
    /// </summary>
    [VerifyTest]
    public void ConstrainedGenericImplementation_ResolvesAndIntercepts()
    {
        var generated = GeneratedAssembly.Create(
            GenericRepo(
                "public class Repo<T> : IRepo<T> where T : class, IMarker, new() { public void Run() { } }",
                supporting: """
                public interface IMarker;

                public class Marked : IMarker;
                """
            )
        );

        var closed = generated.Type("IRepo`1").MakeGenericType(generated.Type("Marked"));
        var resolved = generated.BuildProvider().GetService(closed);

        Verify(resolved is not null);
        Verify(resolved!.GetType().Name.StartsWith("Repo_Intercepted"));
    }

    /// <summary>
    /// The attributes land on whatever <paramref name="implementation"/> declares first, so anything
    /// the implementation needs alongside it goes in <paramref name="supporting"/>.
    /// </summary>
    private static string GenericRepo(string implementation, string supporting = "") =>
        $$"""
            {{Preamble}}

            {{Tracing("Tracing", "tracing")}}

            public interface IRepo<T> { void Run(); }

            {{supporting}}

            [SingletonService]
            [Intercept(typeof(TracingInterceptor))]
            {{implementation}}

            [DependencyModule]
            public partial class TestModule;
            """;

    /// <summary>
    /// A closed construction of a generic service, which is the answer to the refusal above. The
    /// interface is reached through the base rather than declared, and a service registration finds
    /// it that way too — interception used to disagree and report that the class implemented none.
    /// </summary>
    [VerifyTest]
    public void ClosedConstructionOfAGenericService_IsIntercepted()
    {
        var generated = GeneratedAssembly.Create(
            $$"""
            {{Preamble}}

            {{Tracing("Tracing", "tracing")}}

            public interface IRepo<T> { string Name(); }

            public class Repo<T> : IRepo<T> { public string Name() => "repo"; }

            [SingletonService]
            [Intercept(typeof(TracingInterceptor))]
            public class StringRepo : Repo<string> { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = generated.BuildProvider();
        var resolved = provider.GetService(
            generated.Type("IRepo`1").MakeGenericType(typeof(string))
        )!;

        Verify(resolved.GetType().Name.EndsWith("_Intercepted"));
        Verify(Equals(resolved.GetType().GetMethod("Name")!.Invoke(resolved, null), "repo"));
        Verify(Log(generated).SequenceEqual(["enter tracing Name", "exit tracing Name"]));
    }

    [VerifyTest]
    public void RefParameter_ReportsDM0008()
    {
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
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Verify(diagnostic.GetMessage().Contains("by ref"));
    }

    /// <summary>
    /// The invocation state keeps the arguments and the result in fields, and a class cannot hold a
    /// ref struct in a field.
    /// </summary>
    [VerifyTest]
    [Row(
        "System.ReadOnlySpan<char> Name();",
        "public System.ReadOnlySpan<char> Name() => System.MemoryExtensions.AsSpan(\"name\");",
        "returns a ref struct"
    )]
    [Row(
        "int Count(System.ReadOnlySpan<char> text);",
        "public int Count(System.ReadOnlySpan<char> text) => text.Length;",
        "which is a ref struct"
    )]
    [Row(
        "System.ReadOnlySpan<char> Name { get; }",
        "public System.ReadOnlySpan<char> Name => default;",
        "is a ref struct"
    )]
    public void RefStructMember_ReportsDM0008AndWritesNoWrapper(
        string interfaceMember,
        string implementation,
        string reason
    )
    {
        var result = GeneratorTestHarness.Run(Source(interfaceMember, implementation));

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Verify(diagnostic.GetMessage().Contains(reason));
        Verify(!result.GeneratedSources.Keys.Any(name => name.Contains("_Intercepted")));
        result.AssertNoErrors();
    }

    [VerifyTest]
    public void ServiceWithNoInterface_ReportsDM0008()
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            {{Preamble}}

            {{Tracing("Tracing", "tracing")}}

            [SingletonService]
            [Intercept(typeof(TracingInterceptor))]
            public class Standalone { public void Run() { } }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Verify(diagnostic.Severity == DiagnosticSeverity.Warning);
        Verify(diagnostic.GetMessage().Contains("interface"));
    }

    [VerifyTest]
    public void ServiceWithSeveralInterfaces_ReportsDM0008()
    {
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
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Verify(diagnostic.GetMessage().Contains("Service"));
    }

    private static IReadOnlyList<string> Log(GeneratedAssembly generated) =>
        (IReadOnlyList<string>)generated.Type("Recorder").GetField("Entries")!.GetValue(null)!;

    /// <summary>
    /// The gate belongs to the generated assembly, so each compilation gets its own and no test can
    /// release another's.
    /// </summary>
    private static TaskCompletionSource Gate(GeneratedAssembly generated) =>
        (TaskCompletionSource)generated.Type("Recorder").GetField("Gate")!.GetValue(null)!;

    private static object? Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method)!.Invoke(target, arguments);

    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interception;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace TestNamespace;

        public static class Recorder {
            public static readonly List<string> Entries = new();

            // Released by the test. An implementation awaiting this is suspended until the test says
            // otherwise, which is what "the work has not finished yet" needs in order to be a fact
            // rather than a race against a timer.
            public static readonly TaskCompletionSource Gate =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private static readonly string DefaultInterceptor = Tracing("Tracing", "")
        .Replace(" {context.Caller", "{context.Caller");

    private static string Source(
        string interfaceMember,
        string implementation,
        string? interceptor = null
    ) =>
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

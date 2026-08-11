using System.Linq;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Shapes a container has to survive, outside the decorator surface.
/// </summary>
/// <remarks>
/// Each of these is something a real application does and something the generator could plausibly
/// lose quietly — a registration that never happens reads exactly like one that was never asked for.
/// </remarks>
public class RobustnessTests {

    private static string Call(object target, string method) =>
        (string)target.GetType().GetMethod(method)!.Invoke(target, null)!;

    /// <summary>Adding the same module twice registers its services once.</summary>
    /// <remarks>
    /// Composing modules that share a dependency is how module graphs work, so a module arriving
    /// twice is normal rather than a mistake. Registering twice gives two instances behind one
    /// singleton, which is the kind of thing found much later.
    /// </remarks>
    [Fact]
    public void Module_AddedTwice_RegistersItsServicesOnce() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        var module = (IDependencyModule)System.Activator.CreateInstance(assembly.Type("TestModule"))!;

        var services = new ServiceCollection();
        services.AddModules(module, module);

        Assert.Single(
            services.BuildServiceProvider().GetServices(assembly.Type("IGreeter")).Cast<object>());
    }

    /// <summary>Two environment conditions on one service combine with and.</summary>
    [Theory]
    [InlineData("Development", true, 1)]
    [InlineData("Development", false, 0)]
    [InlineData("Production", true, 0)]
    public void Service_WithTwoConditions_RegistersOnlyWhenBothHold(
        string environment, bool flag, int expected) {

        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            [IfEnvironment("Development")]
            [IfEnvironmentValue("feature", "on")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """,
            environment: new ModuleEnvironment(
                environment,
                flag ? new Dictionary<string, string?> { ["feature"] = "on" } : new Dictionary<string, string?>()));

        Assert.Equal(
            expected,
            assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>().Count());
    }

    /// <summary>An explicit service attribute wins over a convention that also matches.</summary>
    [Fact]
    public void Convention_DoesNotAlsoRegisterAnAttributedType() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSingleton();
                }
            }
            """);

        Assert.Single(
            assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>());
    }

    /// <summary>A keyed and an unkeyed registration of one service coexist.</summary>
    [Fact]
    public void Service_KeyedAndUnkeyed_AreBothResolvable() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Plain : IGreeter { public string Greet() => "plain"; }

            [SingletonService(Key = "loud")]
            public class Loud : IGreeter { public string Greet() => "LOUD"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var greeter = assembly.Type("IGreeter");

        Assert.Equal("plain", Call(provider.GetRequiredService(greeter), "Greet"));
        Assert.Equal("LOUD", Call(provider.GetRequiredKeyedService(greeter, "loud"), "Greet"));
    }

    /// <summary>A cross-wired generic service shares one instance across its interfaces.</summary>
    [Fact]
    public void CrossWire_SharesOneInstanceAcrossServiceTypes() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IReader { string Read(); }
            public interface IWriter { string Write(); }

            [CrossWireService]
            public class Store : IReader, IWriter {
                public string Id { get; } = System.Guid.NewGuid().ToString();
                public string Read() => Id;
                public string Write() => Id;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal(
            Call(provider.GetRequiredService(assembly.Type("IReader")), "Read"),
            Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write"));
    }

    /// <summary>A convention excluding a namespace does not register from it.</summary>
    [Fact]
    public void Convention_NotInNamespaces_ExcludesThatNamespace() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace {
                public interface IGreeter { string Greet(); }

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    public void Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IGreeter>()
                            .NotInNamespaces("TestNamespace.Excluded")
                            .AsSingleton();
                    }
                }
            }

            namespace TestNamespace.Included {
                public class Kept : TestNamespace.IGreeter { public string Greet() => "kept"; }
            }

            namespace TestNamespace.Excluded {
                public class Dropped : TestNamespace.IGreeter { public string Greet() => "dropped"; }
            }
            """);

        var all = assembly.BuildProvider().GetServices(assembly.Type("IGreeter"))
            .Cast<object>().Select(g => Call(g, "Greet")).ToArray();

        Assert.Equal(["kept"], all);
    }

    /// <summary>An interceptor sees an async method through to its result.</summary>
    [Fact]
    public async Task Interceptor_OverAnAsyncMethod() {
        var assembly = GeneratedAssembly.Create(
            """
            using System.Threading.Tasks;
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;

            namespace TestNamespace;

            public static class Log { public static int Calls; }

            public interface IFetcher { Task<string> FetchAsync(); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Fetcher : IFetcher {
                public async Task<string> FetchAsync() { await Task.Yield(); return "fetched"; }
            }

            [SingletonService]
            public class CountingInterceptor : IAsyncInterceptor {
                public async ValueTask<TResult> InterceptAsync<TResult>(
                    AsyncInvocationContext<TResult> context) {

                    Log.Calls++;
                    return await context.ProceedAsync();
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));

        var task = (Task<string>)fetcher.GetType().GetMethod("FetchAsync")!.Invoke(fetcher, null)!;

        Assert.Equal("fetched", await task);
        Assert.Equal(1, (int)assembly.Type("Log").GetField("Calls")!.GetValue(null)!);
    }

    /// <summary>A service depending on a collection of a service gets every registration.</summary>
    [Fact]
    public void Service_DependingOnAnEnumerableOfAService_GetsAllOfThem() {
        var assembly = GeneratedAssembly.Create(
            """
            using System.Collections.Generic;
            using System.Linq;
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRule { string Name { get; } }

            [SingletonService]
            public class First : IRule { public string Name => "first"; }

            [SingletonService]
            public class Second : IRule { public string Name => "second"; }

            [SingletonService]
            public class Engine(IEnumerable<IRule> rules) {
                public string Describe() => string.Join(",", rules.Select(r => r.Name));
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("first,second", Call(
            assembly.BuildProvider().GetRequiredService(assembly.Type("Engine")), "Describe"));
    }

    // ------------------------------------------------------------------------------------------
    // Registration. [SingletonService] registers one service type; [CrossWireService] registers
    // every implemented interface against a shared instance. These pin the edges of that split.
    // ------------------------------------------------------------------------------------------

    /// <summary>An explicitly named service type is the one registered.</summary>
    [Fact]
    public void Service_WithAnExplicitServiceType_RegistersThatOne() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IReader { string Read(); }
            public interface IWriter { string Write(); }

            [SingletonService(As = typeof(IWriter))]
            public class Store : IReader, IWriter {
                public string Read() => "read";
                public string Write() => "write";
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("write", Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write"));
        Assert.Null(provider.GetService(assembly.Type("IReader")));
    }

    /// <summary>
    /// A service reaching an interface only through another interface is registered as what it
    /// declares.
    /// </summary>
    /// <remarks>
    /// <c>class Store : IAudited</c> where <c>IAudited : IReader</c>. Whether the base interface is
    /// also a service is a policy question; what must not happen is the declared one going missing.
    /// </remarks>
    [Fact]
    public void Service_ImplementingADerivedInterface_RegistersTheDeclaredOne() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IReader { string Read(); }
            public interface IAudited : IReader { }

            [SingletonService]
            public class Store : IAudited { public string Read() => "read"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("read", Call(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IAudited")), "Read"));
    }

    /// <summary>A generic implementation registers as the open generic it closes nothing of.</summary>
    [Fact]
    public void Service_GenericImplementation_RegistersAsAnOpenGeneric() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRepo<T> { string Name(); }

            [SingletonService]
            public class Repo<T> : IRepo<T> { public string Name() => "repo"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        var closed = assembly.Type("IRepo`1").MakeGenericType(typeof(string));

        Assert.Equal("repo", Call(assembly.BuildProvider().GetRequiredService(closed), "Name"));
    }

    /// <summary>An abstract class is reported rather than registered.</summary>
    /// <remarks>
    /// Emitting the registration produces code that throws when the provider is built, a long way
    /// from the declaration responsible.
    /// </remarks>
    [Fact]
    public void Service_ThatIsAbstract_IsReported() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public abstract class Greeter : IGreeter { public abstract string Greet(); }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0002");
    }

    /// <summary>A record registers like any other class.</summary>
    [Fact]
    public void Service_DeclaredAsARecord() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public record Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("hello", Call(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A service nested inside another type registers under its nested name.</summary>
    [Fact]
    public void Service_NestedInsideAnotherType() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public static class Outer {
                [SingletonService]
                public class Greeter : IGreeter { public string Greet() => "hello"; }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("hello", Call(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>Replace leaves one registration standing, not two.</summary>
    [Fact]
    public void Service_RegisteredWithReplace_LeavesOne() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class First : IGreeter { public string Greet() => "first"; }

            [SingletonService(Using = RegistrationType.Replace)]
            public class Second : IGreeter { public string Greet() => "second"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        var all = assembly.BuildProvider().GetServices(assembly.Type("IGreeter"))
            .Cast<object>().Select(g => Call(g, "Greet")).ToArray();

        Assert.Equal(["second"], all);
    }

    /// <summary>A service whose only constructor is private is reported rather than registered.</summary>
    [Fact]
    public void Service_WithNoAccessibleConstructor_IsReported() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public class Greeter : IGreeter {
                private Greeter() { }
                public string Greet() => "hello";
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSingleton();
                }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0006");
    }

    // ------------------------------------------------------------------------------------------
    // Interception generates a wrapper per service, so every member shape the interface can declare
    // has to be forwarded. The design notes call this the hard part and name shapes that should be
    // refused with a diagnostic rather than mis-generated; these check both halves.
    // ------------------------------------------------------------------------------------------

    private const string InterceptorPreamble =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interception;

        namespace TestNamespace;

        public static class Log { public static int Calls; }

        [SingletonService]
        public class CountingInterceptor : IInterceptor {
            public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                Log.Calls++;
                return context.Proceed();
            }
        }

        """;

    private static GeneratorResult RunIntercepted(string body) =>
        GeneratorTestHarness.Run(InterceptorPreamble + body + """

            [DependencyModule]
            public partial class TestModule;
            """);

    /// <summary>A void method is forwarded.</summary>
    [Fact]
    public void Interceptor_OverAVoidMethod() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { void Work(); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker { public void Work() { } }
            """).Errors);
    }

    /// <summary>A property is forwarded.</summary>
    [Fact]
    public void Interceptor_OverAProperty() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { string Name { get; set; } }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker { public string Name { get; set; } = ""; }
            """).Errors);
    }

    /// <summary>An indexer is forwarded.</summary>
    [Fact]
    public void Interceptor_OverAnIndexer() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { string this[int index] { get; set; } }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public string this[int index] { get => ""; set { } }
            }
            """).Errors);
    }

    /// <summary>An event is forwarded.</summary>
    [Fact]
    public void Interceptor_OverAnEvent() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { event EventHandler? Done; }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker { public event EventHandler? Done; }
            """).Errors);
    }

    /// <summary>A generic method is forwarded with its type parameters.</summary>
    [Fact]
    public void Interceptor_OverAGenericMethod() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { T Echo<T>(T value); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker { public T Echo<T>(T value) => value; }
            """).Errors);
    }

    /// <summary>Default values and params survive forwarding.</summary>
    [Fact]
    public void Interceptor_OverDefaultAndParamsArguments() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { string Join(string separator = ",", params string[] parts); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public string Join(string separator = ",", params string[] parts) =>
                    string.Join(separator, parts);
            }
            """).Errors);
    }

    /// <summary>Members inherited from a base interface are forwarded too.</summary>
    [Fact]
    public void Interceptor_OverAnInheritedInterfaceMember() {
        Assert.Empty(RunIntercepted(
            """
            public interface IBase { string Read(); }
            public interface IWorker : IBase { string Write(); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public string Read() => "read";
                public string Write() => "write";
            }
            """).Errors);
    }

    /// <summary>An IAsyncEnumerable member is forwarded.</summary>
    [Fact]
    public void Interceptor_OverAnAsyncEnumerable() {
        Assert.Empty(RunIntercepted(
            """
            public interface IWorker { IAsyncEnumerable<string> StreamAsync(); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public async IAsyncEnumerable<string> StreamAsync() {
                    await Task.Yield();
                    yield return "one";
                }
            }
            """).Errors);
    }

    /// <summary>
    /// A ref or out parameter is either forwarded or refused, but never mis-generated.
    /// </summary>
    /// <remarks>
    /// The design notes list these as a shape to refuse with a diagnostic, because arguments cannot
    /// round-trip through a reified invocation. Either outcome is acceptable here; a CS error inside
    /// the generated wrapper is not.
    /// </remarks>
    [Fact]
    public void Interceptor_OverRefAndOutParameters_IsForwardedOrRefused() {
        var result = RunIntercepted(
            """
            public interface IWorker { bool TryRead(out string value); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public bool TryRead(out string value) { value = "read"; return true; }
            }
            """);

        Assert.Empty(result.Errors);
    }

    // ------------------------------------------------------------------------------------------
    // The three interceptor contracts, exercised on what they are for: reading and replacing
    // arguments, replacing results, short-circuiting, retrying, and observing failures.
    // ------------------------------------------------------------------------------------------

    private const string ArgumentPreamble =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interception;

        namespace TestNamespace;

        public static class Log {
            public static List<string> Lines = new();
            public static int Calls;
        }

        """;

    private static object Resolve(string body, string serviceName) =>
        GeneratedAssembly.Create(ArgumentPreamble + body + """

            [DependencyModule]
            public partial class TestModule;
            """).BuildProvider().GetRequiredService(
            GeneratedAssembly.Create(ArgumentPreamble + body + """

            [DependencyModule]
            public partial class TestModule;
            """).Type(serviceName));

    private static GeneratedAssembly Build(string body) =>
        GeneratedAssembly.Create(ArgumentPreamble + body + """

            [DependencyModule]
            public partial class TestModule;
            """);

    private static object Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method)!.Invoke(target, arguments)!;

    /// <summary>A synchronous interceptor can rewrite an argument before the call proceeds.</summary>
    /// <remarks>
    /// Replacing an argument is the point of reifying the call. If the wrapper reads the arguments
    /// once and passes its own copies on, the write lands nowhere and the implementation sees the
    /// original — with nothing to show for it.
    /// </remarks>
    [Fact]
    public void Interceptor_CanReplaceAnArgument() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(string name); }

            [SingletonService]
            [Intercept(typeof(RewritingInterceptor))]
            public class Greeter : IGreeter { public string Greet(string name) => "hello " + name; }

            [SingletonService]
            public class RewritingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    context.Arguments[0] = "replaced";
                    return context.Proceed();
                }
            }
            """);

        Assert.Equal(
            "hello replaced",
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet", "original"));
    }

    /// <summary>Arguments carry the names they were declared with.</summary>
    [Fact]
    public void Interceptor_SeesArgumentNamesAndCount() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(string name, int times); }

            [SingletonService]
            [Intercept(typeof(NamingInterceptor))]
            public class Greeter : IGreeter { public string Greet(string name, int times) => name; }

            [SingletonService]
            public class NamingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    for (var i = 0; i < context.Arguments.Count; i++) {
                        Log.Lines.Add(context.Arguments.NameAt(i) + "=" + context.Arguments[i]);
                    }
                    return context.Proceed();
                }
            }
            """);

        Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet", "ian", 2);

        Assert.Equal(
            ["name=ian", "times=2"],
            (List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!);
    }

    /// <summary>An interceptor that never proceeds returns its own result.</summary>
    [Fact]
    public void Interceptor_CanShortCircuitWithoutProceeding() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(); }

            [SingletonService]
            [Intercept(typeof(CachingInterceptor))]
            public class Greeter : IGreeter {
                public string Greet() { Log.Calls++; return "real"; }
            }

            [SingletonService]
            public class CachingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) =>
                    (TResult)(object)"cached";
            }
            """);

        Assert.Equal(
            "cached",
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));

        Assert.Equal(0, (int)assembly.Type("Log").GetField("Calls")!.GetValue(null)!);
    }

    /// <summary>Proceeding twice runs the implementation twice — the retry shape.</summary>
    [Fact]
    public void Interceptor_CanProceedMoreThanOnce() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(); }

            [SingletonService]
            [Intercept(typeof(RetryingInterceptor))]
            public class Greeter : IGreeter {
                public string Greet() { Log.Calls++; return "call" + Log.Calls; }
            }

            [SingletonService]
            public class RetryingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    context.Proceed();
                    return context.Proceed();
                }
            }
            """);

        Assert.Equal(
            "call2",
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));

        Assert.Equal(2, (int)assembly.Type("Log").GetField("Calls")!.GetValue(null)!);
    }

    /// <summary>An interceptor observes an exception the implementation throws.</summary>
    [Fact]
    public void Interceptor_SeesAnExceptionFromTheImplementation() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(); }

            [SingletonService]
            [Intercept(typeof(CatchingInterceptor))]
            public class Greeter : IGreeter {
                public string Greet() => throw new InvalidOperationException("boom");
            }

            [SingletonService]
            public class CatchingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    try { return context.Proceed(); }
                    catch (InvalidOperationException e) {
                        Log.Lines.Add(e.Message);
                        return (TResult)(object)"recovered";
                    }
                }
            }
            """);

        Assert.Equal(
            "recovered",
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));

        Assert.Equal(["boom"], (List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!);
    }

    /// <summary>Two interceptors nest in declaration order, outermost first.</summary>
    [Fact]
    public void Interceptors_NestInDeclarationOrder() {
        var assembly = Build(
            """
            public interface IGreeter { string Greet(); }

            [SingletonService]
            [Intercept(typeof(Outer))]
            [Intercept(typeof(Inner))]
            public class Greeter : IGreeter {
                public string Greet() { Log.Lines.Add("impl"); return "hello"; }
            }

            [SingletonService]
            public class Outer : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Log.Lines.Add("outer");
                    return context.Proceed();
                }
            }

            [SingletonService]
            public class Inner : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Log.Lines.Add("inner");
                    return context.Proceed();
                }
            }
            """);

        Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet");

        Assert.Equal(
            ["outer", "inner", "impl"],
            (List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!);
    }

    /// <summary>An async interceptor can rewrite an argument and replace the result.</summary>
    [Fact]
    public async Task AsyncInterceptor_CanReplaceArgumentsAndResult() {
        var assembly = Build(
            """
            public interface IFetcher { Task<string> FetchAsync(string key); }

            [SingletonService]
            [Intercept(typeof(RewritingAsyncInterceptor))]
            public class Fetcher : IFetcher {
                public async Task<string> FetchAsync(string key) {
                    await Task.Yield();
                    return "fetched:" + key;
                }
            }

            [SingletonService]
            public class RewritingAsyncInterceptor : IAsyncInterceptor {
                public async ValueTask<TResult> InterceptAsync<TResult>(
                    AsyncInvocationContext<TResult> context) {

                    context.Arguments[0] = "replaced";
                    var result = await context.ProceedAsync();
                    return (TResult)(object)((string)(object)result! + ":seen");
                }
            }
            """);

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));
        var task = (Task<string>)Invoke(fetcher, "FetchAsync", "original");

        Assert.Equal("fetched:replaced:seen", await task);
    }

    /// <summary>An async interceptor can short-circuit without awaiting the implementation.</summary>
    [Fact]
    public async Task AsyncInterceptor_CanShortCircuit() {
        var assembly = Build(
            """
            public interface IFetcher { Task<string> FetchAsync(); }

            [SingletonService]
            [Intercept(typeof(ShortCircuitingInterceptor))]
            public class Fetcher : IFetcher {
                public async Task<string> FetchAsync() {
                    Log.Calls++;
                    await Task.Yield();
                    return "real";
                }
            }

            [SingletonService]
            public class ShortCircuitingInterceptor : IAsyncInterceptor {
                public ValueTask<TResult> InterceptAsync<TResult>(
                    AsyncInvocationContext<TResult> context) =>
                    new ValueTask<TResult>((TResult)(object)"cached");
            }
            """);

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));

        Assert.Equal("cached", await (Task<string>)Invoke(fetcher, "FetchAsync"));
        Assert.Equal(0, (int)assembly.Type("Log").GetField("Calls")!.GetValue(null)!);
    }

    /// <summary>A stream interceptor can replace the items the implementation yields.</summary>
    [Fact]
    public async Task StreamInterceptor_CanReplaceTheYieldedItems() {
        var assembly = Build(
            """
            public interface IStreamer { IAsyncEnumerable<string> StreamAsync(string prefix); }

            [SingletonService]
            [Intercept(typeof(StreamRewritingInterceptor))]
            public class Streamer : IStreamer {
                public async IAsyncEnumerable<string> StreamAsync(string prefix) {
                    await Task.Yield();
                    yield return prefix + ":one";
                    yield return prefix + ":two";
                }
            }

            [SingletonService]
            public class StreamRewritingInterceptor : IAsyncEnumerableInterceptor {
                public async IAsyncEnumerable<TItem> InterceptStream<TItem>(
                    StreamInvocationContext<TItem> context) {

                    context.Arguments[0] = "replaced";

                    await foreach (var item in context.Proceed()) {
                        Log.Lines.Add(item!.ToString()!);
                        yield return item;
                    }
                }
            }
            """);

        var streamer = assembly.BuildProvider().GetRequiredService(assembly.Type("IStreamer"));
        var stream = (IAsyncEnumerable<string>)Invoke(streamer, "StreamAsync", "original");

        var seen = new List<string>();
        await foreach (var item in stream) {
            seen.Add(item);
        }

        Assert.Equal(["replaced:one", "replaced:two"], seen);
        Assert.Equal(seen, (List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!);
    }

    /// <summary>A value-type argument round-trips through replacement.</summary>
    /// <remarks>
    /// Arguments are reified as <c>object</c>, so a value type is boxed on the way in and has to be
    /// unboxed back to the parameter's type on the way out.
    /// </remarks>
    [Fact]
    public void Interceptor_CanReplaceAValueTypeArgument() {
        var assembly = Build(
            """
            public interface ICounter { int Add(int value); }

            [SingletonService]
            [Intercept(typeof(DoublingInterceptor))]
            public class Counter : ICounter { public int Add(int value) => value + 1; }

            [SingletonService]
            public class DoublingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    context.Arguments[0] = (int)context.Arguments[0]! * 10;
                    return context.Proceed();
                }
            }
            """);

        Assert.Equal(
            41,
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("ICounter")), "Add", 4));
    }

    // ------------------------------------------------------------------------------------------
    // Module composition. Modules are the unit an application assembles, so what happens when
    // several arrive together — in either order, more than once, depending on each other — is the
    // part a real application exercises hardest.
    // ------------------------------------------------------------------------------------------

    private static object Instance(GeneratedAssembly assembly, string moduleName) =>
        System.Activator.CreateInstance(assembly.Type(moduleName))!;

    /// <summary>Two modules compose, and each contributes its own registrations.</summary>
    [Fact]
    public void Modules_ComposedTogether_BothContribute() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IReader { string Read(); }
            public interface IWriter { string Write(); }

            [SingletonService(Realm = typeof(ReadModule))]
            public class Reader : IReader { public string Read() => "read"; }

            [SingletonService(Realm = typeof(WriteModule))]
            public class Writer : IWriter { public string Write() => "write"; }

            [DependencyModule(OnlyRealm = true)]
            public partial class ReadModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class WriteModule;
            """,
            moduleName: "ReadModule");

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "ReadModule"),
            (IDependencyModule)Instance(assembly, "WriteModule"));

        var provider = services.BuildServiceProvider();

        Assert.Equal("read", Call(provider.GetRequiredService(assembly.Type("IReader")), "Read"));
        Assert.Equal("write", Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write"));
    }

    /// <summary>Composition order does not decide what is available.</summary>
    /// <remarks>
    /// A service in one module depending on one from another has to resolve whichever order the
    /// modules were added, because registration is a phase and resolution happens after all of it.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Modules_CrossModuleDependency_ResolvesInEitherOrder(bool readFirst) {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IReader { string Read(); }

            [SingletonService(Realm = typeof(ReadModule))]
            public class Reader : IReader { public string Read() => "read"; }

            [SingletonService(Realm = typeof(UseModule))]
            public class Consumer(IReader reader) {
                public string Describe() => "using:" + reader.Read();
            }

            [DependencyModule(OnlyRealm = true)]
            public partial class ReadModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class UseModule;
            """,
            moduleName: "ReadModule");

        var read = (IDependencyModule)Instance(assembly, "ReadModule");
        var use = (IDependencyModule)Instance(assembly, "UseModule");

        var services = new ServiceCollection();
        services.AddModules(readFirst ? new[] { read, use } : new[] { use, read });

        Assert.Equal("using:read", Call(
            services.BuildServiceProvider().GetRequiredService(assembly.Type("Consumer")), "Describe"));
    }

    /// <summary>Two equal instances of one module register its services once.</summary>
    /// <remarks>
    /// Composing two modules that each depend on a third is how module graphs work, so the third
    /// arriving twice is normal. Registering twice gives two instances behind one singleton.
    /// </remarks>
    [Fact]
    public void Module_ArrivingTwiceAsSeparateInstances_RegistersOnce() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """);

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "TestModule"),
            (IDependencyModule)Instance(assembly, "TestModule"));

        Assert.Single(
            services.BuildServiceProvider().GetServices(assembly.Type("IGreeter")).Cast<object>());
    }

    /// <summary>A realm-only module contributes nothing to a composition it is not part of.</summary>
    [Fact]
    public void Module_RealmOnly_DoesNotLeakIntoAnotherComposition() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Realm = typeof(HiddenModule))]
            public class Hidden : IGreeter { public string Greet() => "hidden"; }

            [DependencyModule(OnlyRealm = true)]
            public partial class HiddenModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class PlainModule;
            """,
            moduleName: "PlainModule");

        var services = new ServiceCollection();
        services.AddModules((IDependencyModule)Instance(assembly, "PlainModule"));

        Assert.Empty(
            services.BuildServiceProvider().GetServices(assembly.Type("IGreeter")).Cast<object>());
    }

    /// <summary>A decorator in one module wraps a service another module registered.</summary>
    /// <remarks>
    /// Decorations run as a phase after every module's registrations, which is what lets a package
    /// contribute decoration to an application's services. Now that the calls are closed rather than
    /// open, that phase ordering still has to hold.
    /// </remarks>
    [Fact]
    public void Module_DecoratesAServiceAnotherModuleRegistered() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Realm = typeof(ServiceModule))]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator(Realm = typeof(DecoratorModule))]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule(OnlyRealm = true)]
            public partial class ServiceModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class DecoratorModule;
            """,
            moduleName: "ServiceModule");

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "ServiceModule"),
            (IDependencyModule)Instance(assembly, "DecoratorModule"));

        Assert.Equal("HELLO", Call(
            services.BuildServiceProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    // ------------------------------------------------------------------------------------------
    // Environment conditions and convention selectors. Both decide whether a registration happens
    // at all, so getting one wrong is invisible until something is missing at run time.
    // ------------------------------------------------------------------------------------------

    private static GeneratedAssembly WithEnvironment(string body, IModuleEnvironment environment) =>
        GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            """ + body + """

            [DependencyModule]
            public partial class TestModule;
            """,
            environment: environment);

    private static int Count(GeneratedAssembly assembly) =>
        assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>().Count();

    /// <summary>IfNotEnvironment registers everywhere except the names it lists.</summary>
    [Theory]
    [InlineData("Production", 0)]
    [InlineData("Development", 1)]
    public void Condition_IfNotEnvironment(string environment, int expected) {
        Assert.Equal(expected, Count(WithEnvironment(
            """
            [SingletonService]
            [IfNotEnvironment("Production")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }
            """,
            new ModuleEnvironment(environment))));
    }

    /// <summary>One condition listing several names matches any of them.</summary>
    [Theory]
    [InlineData("Development", 1)]
    [InlineData("Staging", 1)]
    [InlineData("Production", 0)]
    public void Condition_IfEnvironment_WithSeveralNames(string environment, int expected) {
        Assert.Equal(expected, Count(WithEnvironment(
            """
            [SingletonService]
            [IfEnvironment("Development", "Staging")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }
            """,
            new ModuleEnvironment(environment))));
    }

    /// <summary>A value condition with no expected value tests only that the key is present.</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Condition_IfEnvironmentValue_KeyPresence(bool present, int expected) {
        Assert.Equal(expected, Count(WithEnvironment(
            """
            [SingletonService]
            [IfEnvironmentValue("feature")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }
            """,
            new ModuleEnvironment(
                "Development",
                present
                    ? new Dictionary<string, string?> { ["feature"] = "anything" }
                    : new Dictionary<string, string?>()))));
    }

    /// <summary>IfNotEnvironmentValue is the negation.</summary>
    [Theory]
    [InlineData("on", 0)]
    [InlineData("off", 1)]
    public void Condition_IfNotEnvironmentValue(string value, int expected) {
        Assert.Equal(expected, Count(WithEnvironment(
            """
            [SingletonService]
            [IfNotEnvironmentValue("feature", "on")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }
            """,
            new ModuleEnvironment(
                "Development", new Dictionary<string, string?> { ["feature"] = value }))));
    }

    /// <summary>A condition on the convention and one on the class combine with and.</summary>
    /// <remarks>
    /// Letting either side win would mean one declaration silently discarding a condition written in
    /// the other, which is the kind of thing nobody finds until production.
    /// </remarks>
    [Theory]
    [InlineData("Development", "on", 1)]
    [InlineData("Development", "off", 0)]
    [InlineData("Production", "on", 0)]
    public void Condition_OnConventionAndOnClass_BothMustHold(
        string environment, string flag, int expected) {

        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [IfEnvironmentValue("feature", "on")]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().IfEnvironment("Development").AsSingleton();
                }
            }
            """,
            environment: new ModuleEnvironment(
                environment, new Dictionary<string, string?> { ["feature"] = flag }));

        Assert.Equal(expected, Count(assembly));
    }

    private static string[] Names(GeneratedAssembly assembly) =>
        assembly.BuildProvider().GetServices(assembly.Type("IGreeter"))
            .Cast<object>().Select(g => Call(g, "Greet")).OrderBy(n => n).ToArray();

    private static GeneratedAssembly WithConvention(string chain) =>
        GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public class MorningGreeter : IGreeter { public string Greet() => "morning"; }
            public class EveningGreeter : IGreeter { public string Greet() => "evening"; }
            public class Salutation : IGreeter { public string Greet() => "salutation"; }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>()CHAIN.AsSingleton();
                }
            }
            """.Replace("CHAIN", chain));

    /// <summary>A name glob selects by the type's own name.</summary>
    [Fact]
    public void Convention_WithName_SelectsByGlob() {
        Assert.Equal(["evening", "morning"], Names(WithConvention(""".WithName("*Greeter")""")));
    }

    /// <summary>An excluding glob removes what it matches.</summary>
    [Fact]
    public void Convention_WithoutName_ExcludesByGlob() {
        Assert.Equal(["evening", "salutation"], Names(WithConvention(""".WithoutName("Morning*")""")));
    }

    /// <summary>A single-character wildcard matches exactly one character.</summary>
    [Fact]
    public void Convention_WithName_SingleCharacterWildcard() {
        Assert.Equal(["salutation"], Names(WithConvention(""".WithName("Salutatio?")""")));
    }

    /// <summary>An attribute filter selects only what carries it.</summary>
    [Fact]
    public void Convention_WithAttribute_SelectsOnlyMarkedTypes() {
        var assembly = GeneratedAssembly.Create(
            """
            using System;
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            [AttributeUsage(AttributeTargets.Class)]
            public class ExportAttribute : Attribute { }

            public interface IGreeter { string Greet(); }

            [Export]
            public class Marked : IGreeter { public string Greet() => "marked"; }

            public class Unmarked : IGreeter { public string Greet() => "unmarked"; }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().WithAttribute<ExportAttribute>().AsSingleton();
                }
            }
            """);

        Assert.Equal(["marked"], Names(assembly));
    }

    /// <summary>An exact-namespace filter does not match a nested namespace.</summary>
    [Fact]
    public void Convention_InExactNamespaces_DoesNotMatchNested() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace {
                public interface IGreeter { string Greet(); }

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    public void Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IGreeter>()
                            .InExactNamespaces("TestNamespace.Direct")
                            .AsSingleton();
                    }
                }
            }

            namespace TestNamespace.Direct {
                public class Here : TestNamespace.IGreeter { public string Greet() => "here"; }
            }

            namespace TestNamespace.Direct.Nested {
                public class Deeper : TestNamespace.IGreeter { public string Greet() => "deeper"; }
            }
            """);

        Assert.Equal(["here"], Names(assembly));
    }
}

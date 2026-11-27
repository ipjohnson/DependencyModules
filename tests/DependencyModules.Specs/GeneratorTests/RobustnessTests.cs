using System.Linq;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Shapes a container has to survive, outside the decorator surface.
/// </summary>
/// <remarks>
/// Each of these is something a real application does and something the generator could plausibly
/// lose quietly — a registration that never happens reads exactly like one that was never asked for.
/// </remarks>
public partial class RobustnessTests
{
    private static string Call(object target, string method) =>
        (string)target.GetType().GetMethod(method)!.Invoke(target, null)!;

    /// <summary>Adding the same module twice registers its services once.</summary>
    /// <remarks>
    /// Composing modules that share a dependency is how module graphs work, so a module arriving
    /// twice is normal rather than a mistake. Registering twice gives two instances behind one
    /// singleton, which is the kind of thing found much later.
    /// </remarks>
    [VerifyTest]
    public void Module_AddedTwice_RegistersItsServicesOnce()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var module = (IDependencyModule)
            System.Activator.CreateInstance(assembly.Type("TestModule"))!;

        var services = new ServiceCollection();
        services.AddModules(module, module);

        Single(
            services.BuildServiceProvider().GetServices(assembly.Type("IGreeter")).Cast<object>()
        );
    }

    /// <summary>Two environment conditions on one service combine with and.</summary>
    [VerifyTest]
    [Row("Development", true, 1)]
    [Row("Development", false, 0)]
    [Row("Production", true, 0)]
    public void Service_WithTwoConditions_RegistersOnlyWhenBothHold(
        string environment,
        bool flag,
        int expected
    )
    {
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
                flag
                    ? new Dictionary<string, string?> { ["feature"] = "on" }
                    : new Dictionary<string, string?>()
            )
        );

        Verify(
            assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>().Count()
                == expected
        );
    }

    /// <summary>An explicit service attribute wins over a convention that also matches.</summary>
    [VerifyTest]
    public void Convention_DoesNotAlsoRegisterAnAttributedType()
    {
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
            """
        );

        Single(assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>());
    }

    /// <summary>A keyed and an unkeyed registration of one service coexist.</summary>
    [VerifyTest]
    public void Service_KeyedAndUnkeyed_AreBothResolvable()
    {
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
            """
        );

        var provider = assembly.BuildProvider();
        var greeter = assembly.Type("IGreeter");

        Verify(Call(provider.GetRequiredService(greeter), "Greet") == "plain");
        Verify(Call(provider.GetRequiredKeyedService(greeter, "loud"), "Greet") == "LOUD");
    }

    /// <summary>A cross-wired generic service shares one instance across its interfaces.</summary>
    [VerifyTest]
    public void CrossWire_SharesOneInstanceAcrossServiceTypes()
    {
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
            """
        );

        var provider = assembly.BuildProvider();

        Verify(
            Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write")
                == Call(provider.GetRequiredService(assembly.Type("IReader")), "Read")
        );
    }

    /// <summary>A convention excluding a namespace does not register from it.</summary>
    [VerifyTest]
    public void Convention_NotInNamespaces_ExcludesThatNamespace()
    {
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
            """
        );

        var all = assembly
            .BuildProvider()
            .GetServices(assembly.Type("IGreeter"))
            .Cast<object>()
            .Select(g => Call(g, "Greet"))
            .ToArray();

        Verify(all.SequenceEqual(["kept"]));
    }

    /// <summary>An interceptor sees an async method through to its result.</summary>
    [VerifyTest]
    public async Task Interceptor_OverAnAsyncMethod()
    {
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
            """
        );

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));

        var task = (Task<string>)fetcher.GetType().GetMethod("FetchAsync")!.Invoke(fetcher, null)!;

        var fetched = await task;

        Verify(fetched == "fetched");
        Verify((int)assembly.Type("Log").GetField("Calls")!.GetValue(null)! == 1);
    }

    /// <summary>A service depending on a collection of a service gets every registration.</summary>
    [VerifyTest]
    public void Service_DependingOnAnEnumerableOfAService_GetsAllOfThem()
    {
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
            """
        );

        Verify(
            Call(assembly.BuildProvider().GetRequiredService(assembly.Type("Engine")), "Describe")
                == "first,second"
        );
    }

    // ------------------------------------------------------------------------------------------
    // Registration. [SingletonService] registers one service type; [CrossWireService] registers
    // every implemented interface against a shared instance. These pin the edges of that split.
    // ------------------------------------------------------------------------------------------

    /// <summary>An explicitly named service type is the one registered.</summary>
    [VerifyTest]
    public void Service_WithAnExplicitServiceType_RegistersThatOne()
    {
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
            """
        );

        var provider = assembly.BuildProvider();

        Verify(Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write") == "write");
        Verify(provider.GetService(assembly.Type("IReader")) is null);
    }

    /// <summary>
    /// A service reaching an interface only through another interface is registered as what it
    /// declares.
    /// </summary>
    /// <remarks>
    /// <c>class Store : IAudited</c> where <c>IAudited : IReader</c>. Whether the base interface is
    /// also a service is a policy question; what must not happen is the declared one going missing.
    /// </remarks>
    [VerifyTest]
    public void Service_ImplementingADerivedInterface_RegistersTheDeclaredOne()
    {
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
            """
        );

        Verify(
            Call(assembly.BuildProvider().GetRequiredService(assembly.Type("IAudited")), "Read")
                == "read"
        );
    }

    /// <summary>A generic implementation registers as the open generic it closes nothing of.</summary>
    [VerifyTest]
    public void Service_GenericImplementation_RegistersAsAnOpenGeneric()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRepo<T> { string Name(); }

            [SingletonService]
            public class Repo<T> : IRepo<T> { public string Name() => "repo"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var closed = assembly.Type("IRepo`1").MakeGenericType(typeof(string));

        Verify(Call(assembly.BuildProvider().GetRequiredService(closed), "Name") == "repo");
    }

    /// <summary>An abstract class is reported rather than registered.</summary>
    /// <remarks>
    /// Emitting the registration produces code that throws when the provider is built, a long way
    /// from the declaration responsible.
    /// </remarks>
    [VerifyTest]
    public void Service_ThatIsAbstract_IsReported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public abstract class Greeter : IGreeter { public abstract string Greet(); }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(!result.Errors.Any());
        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0002"));
    }

    /// <summary>A record registers like any other class.</summary>
    [VerifyTest]
    public void Service_DeclaredAsARecord()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public record Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(
            Call(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet")
                == "hello"
        );
    }

    /// <summary>A service nested inside another type registers under its nested name.</summary>
    [VerifyTest]
    public void Service_NestedInsideAnotherType()
    {
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
            """
        );

        Verify(
            Call(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet")
                == "hello"
        );
    }

    /// <summary>Replace leaves one registration standing, not two.</summary>
    [VerifyTest]
    public void Service_RegisteredWithReplace_LeavesOne()
    {
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
            """
        );

        var all = assembly
            .BuildProvider()
            .GetServices(assembly.Type("IGreeter"))
            .Cast<object>()
            .Select(g => Call(g, "Greet"))
            .ToArray();

        Verify(all.SequenceEqual(["second"]));
    }

    /// <summary>A service whose only constructor is private is reported rather than registered.</summary>
    [VerifyTest]
    public void Service_WithNoAccessibleConstructor_IsReported()
    {
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
            """
        );

        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0006"));
    }

    // ------------------------------------------------------------------------------------------
    // Interception generates a wrapper per service, so every member shape the interface can declare
    // has to be forwarded. The design notes call this the hard part and name shapes that should be
    // refused with a diagnostic rather than mis-generated; these check both halves.
    // ------------------------------------------------------------------------------------------

    private const string InterceptorPreamble = """
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
        GeneratorTestHarness.Run(
            InterceptorPreamble
                + body
                + """

                [DependencyModule]
                public partial class TestModule;
                """
        );

    /// <summary>A void method is forwarded.</summary>
    [VerifyTest]
    public void Interceptor_OverAVoidMethod()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { void Work(); }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker { public void Work() { } }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>A property is forwarded.</summary>
    [VerifyTest]
    public void Interceptor_OverAProperty()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { string Name { get; set; } }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker { public string Name { get; set; } = ""; }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>An indexer is forwarded.</summary>
    [VerifyTest]
    public void Interceptor_OverAnIndexer()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { string this[int index] { get; set; } }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker {
                    public string this[int index] { get => ""; set { } }
                }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>An event is forwarded.</summary>
    [VerifyTest]
    public void Interceptor_OverAnEvent()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { event EventHandler? Done; }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker { public event EventHandler? Done; }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>A generic method is forwarded with its type parameters.</summary>
    [VerifyTest]
    public void Interceptor_OverAGenericMethod()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { T Echo<T>(T value); }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker { public T Echo<T>(T value) => value; }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>Default values and params survive forwarding.</summary>
    [VerifyTest]
    public void Interceptor_OverDefaultAndParamsArguments()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IWorker { string Join(string separator = ",", params string[] parts); }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker {
                    public string Join(string separator = ",", params string[] parts) =>
                        string.Join(separator, parts);
                }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>Members inherited from a base interface are forwarded too.</summary>
    [VerifyTest]
    public void Interceptor_OverAnInheritedInterfaceMember()
    {
        Verify(
            !RunIntercepted(
                """
                public interface IBase { string Read(); }
                public interface IWorker : IBase { string Write(); }

                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public class Worker : IWorker {
                    public string Read() => "read";
                    public string Write() => "write";
                }
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>An IAsyncEnumerable member is forwarded.</summary>
    [VerifyTest]
    public void Interceptor_OverAnAsyncEnumerable()
    {
        Verify(
            !RunIntercepted(
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
                """
            )
                .Errors.Any()
        );
    }

    /// <summary>
    /// A ref or out parameter is either forwarded or refused, but never mis-generated.
    /// </summary>
    /// <remarks>
    /// The design notes list these as a shape to refuse with a diagnostic, because arguments cannot
    /// round-trip through a reified invocation. Either outcome is acceptable here; a CS error inside
    /// the generated wrapper is not.
    /// </remarks>
    [VerifyTest]
    public void Interceptor_OverRefAndOutParameters_IsForwardedOrRefused()
    {
        var result = RunIntercepted(
            """
            public interface IWorker { bool TryRead(out string value); }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class Worker : IWorker {
                public bool TryRead(out string value) { value = "read"; return true; }
            }
            """
        );

        Verify(!result.Errors.Any());
    }

    // ------------------------------------------------------------------------------------------
    // The three interceptor contracts, exercised on what they are for: reading and replacing
    // arguments, replacing results, short-circuiting, retrying, and observing failures.
    // ------------------------------------------------------------------------------------------

    private const string ArgumentPreamble = """
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
        GeneratedAssembly
            .Create(
                ArgumentPreamble
                    + body
                    + """

                    [DependencyModule]
                    public partial class TestModule;
                    """
            )
            .BuildProvider()
            .GetRequiredService(
                GeneratedAssembly
                    .Create(
                        ArgumentPreamble
                            + body
                            + """

                            [DependencyModule]
                            public partial class TestModule;
                            """
                    )
                    .Type(serviceName)
            );

    private static GeneratedAssembly Build(string body) =>
        GeneratedAssembly.Create(
            ArgumentPreamble
                + body
                + """

                [DependencyModule]
                public partial class TestModule;
                """
        );

    private static object Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method)!.Invoke(target, arguments)!;

    /// <summary>A synchronous interceptor can rewrite an argument before the call proceeds.</summary>
    /// <remarks>
    /// Replacing an argument is the point of reifying the call. If the wrapper reads the arguments
    /// once and passes its own copies on, the write lands nowhere and the implementation sees the
    /// original — with nothing to show for it.
    /// </remarks>
    [VerifyTest]
    public void Interceptor_CanReplaceAnArgument()
    {
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
            """
        );

        Verify(
            Invoke(
                assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")),
                "Greet",
                "original"
            )
                is "hello replaced"
        );
    }

    /// <summary>Arguments carry the names they were declared with.</summary>
    [VerifyTest]
    public void Interceptor_SeesArgumentNamesAndCount()
    {
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
            """
        );

        Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")),
            "Greet",
            "ian",
            2
        );

        Verify(
            ((List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!).SequenceEqual([
                "name=ian",
                "times=2",
            ])
        );
    }

    /// <summary>An interceptor that never proceeds returns its own result.</summary>
    [VerifyTest]
    public void Interceptor_CanShortCircuitWithoutProceeding()
    {
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
            """
        );

        Verify(
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet")
                is "cached"
        );

        Verify((int)assembly.Type("Log").GetField("Calls")!.GetValue(null)! == 0);
    }

    /// <summary>Proceeding twice runs the implementation twice — the retry shape.</summary>
    [VerifyTest]
    public void Interceptor_CanProceedMoreThanOnce()
    {
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
            """
        );

        Verify(
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet")
                is "call2"
        );

        Verify((int)assembly.Type("Log").GetField("Calls")!.GetValue(null)! == 2);
    }

    /// <summary>An interceptor observes an exception the implementation throws.</summary>
    [VerifyTest]
    public void Interceptor_SeesAnExceptionFromTheImplementation()
    {
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
            """
        );

        Verify(
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet")
                is "recovered"
        );

        Verify(
            ((List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!).SequenceEqual([
                "boom",
            ])
        );
    }

    /// <summary>Two interceptors nest in declaration order, outermost first.</summary>
    [VerifyTest]
    public void Interceptors_NestInDeclarationOrder()
    {
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
            """
        );

        Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet");

        Verify(
            ((List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!).SequenceEqual([
                "outer",
                "inner",
                "impl",
            ])
        );
    }

    /// <summary>An async interceptor can rewrite an argument and replace the result.</summary>
    [VerifyTest]
    public async Task AsyncInterceptor_CanReplaceArgumentsAndResult()
    {
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
            """
        );

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));
        var task = (Task<string>)Invoke(fetcher, "FetchAsync", "original");

        var fetched = await task;

        Verify(fetched == "fetched:replaced:seen");
    }

    /// <summary>An async interceptor can short-circuit without awaiting the implementation.</summary>
    [VerifyTest]
    public async Task AsyncInterceptor_CanShortCircuit()
    {
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
            """
        );

        var fetcher = assembly.BuildProvider().GetRequiredService(assembly.Type("IFetcher"));

        var fetched = await (Task<string>)Invoke(fetcher, "FetchAsync");

        Verify(fetched == "cached");
        Verify((int)assembly.Type("Log").GetField("Calls")!.GetValue(null)! == 0);
    }

    /// <summary>A stream interceptor can replace the items the implementation yields.</summary>
    [VerifyTest]
    public async Task StreamInterceptor_CanReplaceTheYieldedItems()
    {
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
            """
        );

        var streamer = assembly.BuildProvider().GetRequiredService(assembly.Type("IStreamer"));
        var stream = (IAsyncEnumerable<string>)Invoke(streamer, "StreamAsync", "original");

        var seen = new List<string>();
        await foreach (var item in stream)
        {
            seen.Add(item);
        }

        Verify(seen.SequenceEqual(["replaced:one", "replaced:two"]));
        Verify(
            ((List<string>)assembly.Type("Log").GetField("Lines")!.GetValue(null)!).SequenceEqual(
                seen
            )
        );
    }

    /// <summary>A value-type argument round-trips through replacement.</summary>
    /// <remarks>
    /// Arguments are reified as <c>object</c>, so a value type is boxed on the way in and has to be
    /// unboxed back to the parameter's type on the way out.
    /// </remarks>
    [VerifyTest]
    public void Interceptor_CanReplaceAValueTypeArgument()
    {
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
            """
        );

        Verify(
            Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("ICounter")), "Add", 4)
                is 41
        );
    }

    // ------------------------------------------------------------------------------------------
    // Module composition. Modules are the unit an application assembles, so what happens when
    // several arrive together — in either order, more than once, depending on each other — is the
    // part a real application exercises hardest.
    // ------------------------------------------------------------------------------------------

    private static object Instance(GeneratedAssembly assembly, string moduleName) =>
        System.Activator.CreateInstance(assembly.Type(moduleName))!;

    /// <summary>Two modules compose, and each contributes its own registrations.</summary>
    [VerifyTest]
    public void Modules_ComposedTogether_BothContribute()
    {
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
            moduleName: "ReadModule"
        );

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "ReadModule"),
            (IDependencyModule)Instance(assembly, "WriteModule")
        );

        var provider = services.BuildServiceProvider();

        Verify(Call(provider.GetRequiredService(assembly.Type("IReader")), "Read") == "read");
        Verify(Call(provider.GetRequiredService(assembly.Type("IWriter")), "Write") == "write");
    }

    /// <summary>Composition order does not decide what is available.</summary>
    /// <remarks>
    /// A service in one module depending on one from another has to resolve whichever order the
    /// modules were added, because registration is a phase and resolution happens after all of it.
    /// </remarks>
    [VerifyTest]
    [Row(true)]
    [Row(false)]
    public void Modules_CrossModuleDependency_ResolvesInEitherOrder(bool readFirst)
    {
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
            moduleName: "ReadModule"
        );

        var read = (IDependencyModule)Instance(assembly, "ReadModule");
        var use = (IDependencyModule)Instance(assembly, "UseModule");

        var services = new ServiceCollection();
        services.AddModules(readFirst ? new[] { read, use } : new[] { use, read });

        Verify(
            Call(
                services.BuildServiceProvider().GetRequiredService(assembly.Type("Consumer")),
                "Describe"
            ) == "using:read"
        );
    }

    /// <summary>Two equal instances of one module register its services once.</summary>
    /// <remarks>
    /// Composing two modules that each depend on a third is how module graphs work, so the third
    /// arriving twice is normal. Registering twice gives two instances behind one singleton.
    /// </remarks>
    [VerifyTest]
    public void Module_ArrivingTwiceAsSeparateInstances_RegistersOnce()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "TestModule"),
            (IDependencyModule)Instance(assembly, "TestModule")
        );

        Single(
            services.BuildServiceProvider().GetServices(assembly.Type("IGreeter")).Cast<object>()
        );
    }

    /// <summary>A realm-only module contributes nothing to a composition it is not part of.</summary>
    [VerifyTest]
    public void Module_RealmOnly_DoesNotLeakIntoAnotherComposition()
    {
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
            moduleName: "PlainModule"
        );

        var services = new ServiceCollection();
        services.AddModules((IDependencyModule)Instance(assembly, "PlainModule"));

        Verify(
            !services
                .BuildServiceProvider()
                .GetServices(assembly.Type("IGreeter"))
                .Cast<object>()
                .Any()
        );
    }

    /// <summary>A decorator in one module wraps a service another module registered.</summary>
    /// <remarks>
    /// Decorations run as a phase after every module's registrations, which is what lets a package
    /// contribute decoration to an application's services. Now that the calls are closed rather than
    /// open, that phase ordering still has to hold.
    /// </remarks>
    [VerifyTest]
    public void Module_DecoratesAServiceAnotherModuleRegistered()
    {
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
            moduleName: "ServiceModule"
        );

        var services = new ServiceCollection();

        services.AddModules(
            (IDependencyModule)Instance(assembly, "ServiceModule"),
            (IDependencyModule)Instance(assembly, "DecoratorModule")
        );

        Verify(
            Call(
                services.BuildServiceProvider().GetRequiredService(assembly.Type("IGreeter")),
                "Greet"
            ) == "HELLO"
        );
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

            """
                + body
                + """

                [DependencyModule]
                public partial class TestModule;
                """,
            environment: environment
        );

    private static int Count(GeneratedAssembly assembly) =>
        assembly.BuildProvider().GetServices(assembly.Type("IGreeter")).Cast<object>().Count();

    /// <summary>IfNotEnvironment registers everywhere except the names it lists.</summary>
    [VerifyTest]
    [Row("Production", 0)]
    [Row("Development", 1)]
    public void Condition_IfNotEnvironment(string environment, int expected)
    {
        var count = Count(
            WithEnvironment(
                """
                [SingletonService]
                [IfNotEnvironment("Production")]
                public class Greeter : IGreeter { public string Greet() => "hello"; }
                """,
                new ModuleEnvironment(environment)
            )
        );

        Verify(count == expected);
    }

    /// <summary>One condition listing several names matches any of them.</summary>
    [VerifyTest]
    [Row("Development", 1)]
    [Row("Staging", 1)]
    [Row("Production", 0)]
    public void Condition_IfEnvironment_WithSeveralNames(string environment, int expected)
    {
        var count = Count(
            WithEnvironment(
                """
                [SingletonService]
                [IfEnvironment("Development", "Staging")]
                public class Greeter : IGreeter { public string Greet() => "hello"; }
                """,
                new ModuleEnvironment(environment)
            )
        );

        Verify(count == expected);
    }

    /// <summary>A value condition with no expected value tests only that the key is present.</summary>
    [VerifyTest]
    [Row(true, 1)]
    [Row(false, 0)]
    public void Condition_IfEnvironmentValue_KeyPresence(bool present, int expected)
    {
        var count = Count(
            WithEnvironment(
                """
                [SingletonService]
                [IfEnvironmentValue("feature")]
                public class Greeter : IGreeter { public string Greet() => "hello"; }
                """,
                new ModuleEnvironment(
                    "Development",
                    present
                        ? new Dictionary<string, string?> { ["feature"] = "anything" }
                        : new Dictionary<string, string?>()
                )
            )
        );

        Verify(count == expected);
    }

    /// <summary>IfNotEnvironmentValue is the negation.</summary>
    [VerifyTest]
    [Row("on", 0)]
    [Row("off", 1)]
    public void Condition_IfNotEnvironmentValue(string value, int expected)
    {
        var count = Count(
            WithEnvironment(
                """
                [SingletonService]
                [IfNotEnvironmentValue("feature", "on")]
                public class Greeter : IGreeter { public string Greet() => "hello"; }
                """,
                new ModuleEnvironment(
                    "Development",
                    new Dictionary<string, string?> { ["feature"] = value }
                )
            )
        );

        Verify(count == expected);
    }

    /// <summary>A condition on the convention and one on the class combine with and.</summary>
    /// <remarks>
    /// Letting either side win would mean one declaration silently discarding a condition written in
    /// the other, which is the kind of thing nobody finds until production.
    /// </remarks>
    [VerifyTest]
    [Row("Development", "on", 1)]
    [Row("Development", "off", 0)]
    [Row("Production", "on", 0)]
    public void Condition_OnConventionAndOnClass_BothMustHold(
        string environment,
        string flag,
        int expected
    )
    {
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
                environment,
                new Dictionary<string, string?> { ["feature"] = flag }
            )
        );

        Verify(Count(assembly) == expected);
    }

    private static string[] Names(GeneratedAssembly assembly) =>
        assembly
            .BuildProvider()
            .GetServices(assembly.Type("IGreeter"))
            .Cast<object>()
            .Select(g => Call(g, "Greet"))
            .OrderBy(n => n)
            .ToArray();

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
            """.Replace("CHAIN", chain)
        );

    /// <summary>A name glob selects by the type's own name.</summary>
    [VerifyTest]
    public void Convention_WithName_SelectsByGlob()
    {
        Verify(
            Names(WithConvention(""".WithName("*Greeter")""")).SequenceEqual(["evening", "morning"])
        );
    }

    /// <summary>An excluding glob removes what it matches.</summary>
    [VerifyTest]
    public void Convention_WithoutName_ExcludesByGlob()
    {
        Verify(
            Names(WithConvention(""".WithoutName("Morning*")"""))
                .SequenceEqual(["evening", "salutation"])
        );
    }

    /// <summary>A single-character wildcard matches exactly one character.</summary>
    [VerifyTest]
    public void Convention_WithName_SingleCharacterWildcard()
    {
        Verify(Names(WithConvention(""".WithName("Salutatio?")""")).SequenceEqual(["salutation"]));
    }

    /// <summary>An attribute filter selects only what carries it.</summary>
    [VerifyTest]
    public void Convention_WithAttribute_SelectsOnlyMarkedTypes()
    {
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
            """
        );

        Verify(Names(assembly).SequenceEqual(["marked"]));
    }

    /// <summary>An exact-namespace filter does not match a nested namespace.</summary>
    [VerifyTest]
    public void Convention_InExactNamespaces_DoesNotMatchNested()
    {
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
            """
        );

        Verify(Names(assembly).SequenceEqual(["here"]));
    }
}

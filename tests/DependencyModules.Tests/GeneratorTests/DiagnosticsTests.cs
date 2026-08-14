using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator's job is to move failures from run time to build time. Each of these covers a
/// mistake that previously produced either a crash when the container was built or, worse, a
/// successful build that quietly registered nothing.
/// </summary>
public class DiagnosticsTests {

    /// <summary>
    /// An abstract implementation used to be registered anyway, and the resulting
    /// AddSingleton(typeof(IThing), typeof(AbstractThing)) threw when the provider was built,
    /// a long way from the declaration responsible.
    /// </summary>
    [Fact]
    public void AbstractService_ReportsDM0002() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public abstract class Thing : IThing;"));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0002");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Thing", diagnostic.GetMessage());
        Assert.Contains("abstract", diagnostic.GetMessage());
    }

    [Fact]
    public void AbstractService_IsNotRegistered() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public abstract class Thing : IThing;"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Dependencies"));
    }

    [Fact]
    public void StaticService_ReportsDM0002() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [SingletonService]
            public static class StaticThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0002");

        Assert.Contains("static", diagnostic.GetMessage());
    }

    /// <summary>
    /// A concrete service alongside an unconstructable one must still be registered; reporting the
    /// bad one should not discard the good ones.
    /// </summary>
    [Fact]
    public void ConcreteServices_AreStillRegisteredAlongsideARejectedOne() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;
            public interface IAbstract;

            [SingletonService] public class Thing : IThing;

            [SingletonService] public abstract class AbstractThing : IAbstract;

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();

        Assert.NotNull(provider.GetService(generated.Type("IThing")));
        Assert.Null(provider.GetService(generated.Type("IAbstract")));
    }

    /// <summary>
    /// The compiler reports CS0260 once the generated half arrives, but that describes the symptom.
    /// DM0003 names the fix, and generation is skipped so it is the only error shown.
    /// </summary>
    [Fact]
    public void NonPartialModule_ReportsDM0003() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService] public class Thing : IThing;

            [DependencyModule]
            public class NotPartialModule;
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0003");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("NotPartialModule", diagnostic.GetMessage());
        Assert.Contains("partial", diagnostic.GetMessage());
    }

    [Fact]
    public void NonPartialModule_DoesNotGenerateAConflictingDeclaration() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public class NotPartialModule;
            """);

        // Emitting the module half would add CS0260 on top of DM0003 and point at the wrong thing.
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("NotPartialModule.Module"));
        Assert.DoesNotContain(result.CompilationDiagnostics, d => d.Id == "CS0260");
    }

    [Fact]
    public void PartialModule_ReportsNothing() {
        var result = GeneratorTestHarness.Run(Module("[SingletonService] public class Thing : IThing;"));

        result.AssertNoErrors();
        Assert.Empty(result.GeneratorDiagnostics);
    }

    [Fact]
    public void AbstractFactoryHost_IsAllowedBecauseTheFactorySuppliesTheInstance() {
        // The declaring type is never constructed, so an abstract host is legitimate here.
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            public abstract class Factories {
                [SingletonService]
                public static IThing Create() => null!;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0002");
    }

    /// <summary>
    /// A generic decorator declared on the class, over a service registered as an open generic. The
    /// expansion has no closed construction to close over, so the declaration used to disappear with
    /// a green build — a decorator in the source that never ran.
    /// </summary>
    [Fact]
    public void GenericDecoratorOverOpenGenericRegistration_ReportsDM0013() {
        var result = GeneratorTestHarness.Run(
            OpenGenericStore(
                """
                [Decorator]
                public class LoggingStore<T>(IStore<T> inner) : IStore<T> {
                    public string Read(T key) => inner.Read(key);
                }
                """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0013");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("IStore", diagnostic.GetMessage());
        Assert.Contains("LoggingStore", diagnostic.GetMessage());
    }

    /// <summary>
    /// The same service, decorated from the module instead. Both declaration forms reach the same
    /// expansion, so both have to report.
    /// </summary>
    [Fact]
    public void ModuleDeclaredDecoratorOverOpenGenericRegistration_ReportsDM0013() {
        var result = GeneratorTestHarness.Run(
            OpenGenericStore(
                """
                public class LoggingStore<T>(IStore<T> inner) : IStore<T> {
                    public string Read(T key) => inner.Read(key);
                }
                """,
                moduleAttributes: "[Decorate(typeof(IStore<>), typeof(LoggingStore<>))]"));

        Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0013");
    }

    /// <summary>
    /// A <i>non-generic</i> decorator named against an unbound service type. This one needed no
    /// expansion, so nothing caught it and it reached emission still carrying <c>IStore&lt;&gt;</c> —
    /// which is CS7003 in generated code.
    /// </summary>
    [Fact]
    public void NonGenericDecoratorOverOpenGenericRegistration_ReportsDM0013() {
        var result = GeneratorTestHarness.Run(
            OpenGenericStore(
                """
                public class StringStoreDecorator(IStore<string> inner) : IStore<string> {
                    public string Read(string key) => inner.Read(key);
                }
                """,
                moduleAttributes: "[Decorate(typeof(IStore<>), typeof(StringStoreDecorator))]"));

        Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0013");
    }

    /// <summary>
    /// And the generated code compiles, which it did not before: <c>GeneratedAssembly.Create</c>
    /// asserts the compilation is clean and emits.
    /// </summary>
    [Fact]
    public void NonGenericDecoratorOverOpenGenericRegistration_StillCompiles() {
        var generated = GeneratedAssembly.Create(
            OpenGenericStore(
                """
                public class StringStoreDecorator(IStore<string> inner) : IStore<string> {
                    public string Read(string key) => inner.Read(key);
                }
                """,
                moduleAttributes: "[Decorate(typeof(IStore<>), typeof(StringStoreDecorator))]"));

        Assert.Contains(generated.Services, d => d.ServiceType == generated.Type("IStore`1"));
    }

    /// <summary>
    /// The case that must keep working: closed registrations, which a generic decorator is expanded
    /// across. This is the shape a MediatR-style pipeline is built from.
    /// </summary>
    [Fact]
    public void GenericDecoratorOverClosedRegistrations_DoesNotReportDM0013() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IStore<T> { string Read(T key); }

            [SingletonService] public class IntStore : IStore<int> { public string Read(int key) => "int"; }
            [SingletonService] public class StringStore : IStore<string> { public string Read(string key) => "string"; }

            [Decorator]
            public class LoggingStore<T>(IStore<T> inner) : IStore<T> {
                public string Read(T key) => inner.Read(key);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0013");
    }

    /// <summary>
    /// A decorator naming a service this compilation does not register at all stays quiet. Naming a
    /// service someone else registers is what <c>[Decorate]</c> is for, so reporting here would fire
    /// on the feature's primary use.
    /// </summary>
    [Fact]
    public void DecoratorForAServiceThisCompilationDoesNotRegister_DoesNotReportDM0013() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IElsewhere<T> { string Read(T key); }

            public class LoggingElsewhere<T>(IElsewhere<T> inner) : IElsewhere<T> {
                public string Read(T key) => inner.Read(key);
            }

            [DependencyModule]
            [Decorate(typeof(IElsewhere<>), typeof(LoggingElsewhere<>))]
            public partial class TestModule;
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0013");
    }

    /// <summary>
    /// Cross-wiring shares one instance across every service type, which needs a factory — and an
    /// open generic registration cannot have one. The emission was invalid on its face: the type
    /// parameter leaked into <c>typeof(ILedger&lt;T&gt;)</c> beside
    /// <c>GetRequiredService&lt;Ledger&lt;&gt;&gt;()</c>.
    /// </summary>
    [Fact]
    public void CrossWiredGenericType_ReportsDM0014() {
        var result = GeneratorTestHarness.Run(CrossWiredLedger("public class Ledger<T> : ILedger<T>, IAudit<T>;"));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0014");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Ledger", diagnostic.GetMessage());
    }

    [Fact]
    public void CrossWiredGenericType_IsNotRegistered() {
        var result = GeneratorTestHarness.Run(CrossWiredLedger("public class Ledger<T> : ILedger<T>, IAudit<T>;"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Dependencies"));
    }

    /// <summary>
    /// Cross-wiring a non-generic type is untouched, and still shares one instance across both
    /// interfaces.
    /// </summary>
    [Fact]
    public void CrossWiredNonGenericType_StillRegisters() {
        var generated = GeneratedAssembly.Create(CrossWiredLedger("public class Ledger : ILedger<int>, IAudit<int>;"));

        var provider = generated.BuildProvider();

        // The point of cross-wiring: both interfaces answer with the one instance.
        Assert.Same(
            provider.GetService(generated.Type("ILedger`1").MakeGenericType(typeof(int))),
            provider.GetService(generated.Type("IAudit`1").MakeGenericType(typeof(int))));
    }

    /// <summary>
    /// An interceptor implementing only <c>IInterceptor</c>, applied to a service whose members are
    /// all async. It never runs, and the build used to be green — the model was ignored before
    /// anything could report on it.
    /// </summary>
    [Fact]
    public void InterceptorThatServesNoMember_ReportsDM0015() {
        var result = GeneratorTestHarness.Run(
            Intercepted(
                """
                public interface IAsyncOnly {
                    Task<string> GetAsync(string key);
                }

                [SingletonService]
                [Intercept(typeof(SyncOnlyInterceptor))]
                public class AsyncOnly : IAsyncOnly {
                    public Task<string> GetAsync(string key) => Task.FromResult(key);
                }
                """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0015");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("SyncOnlyInterceptor", diagnostic.GetMessage());
        Assert.Contains("IAsyncInterceptor", diagnostic.GetMessage());
        Assert.Contains("GetAsync", diagnostic.GetMessage());
    }

    /// <summary>
    /// The partial case: the interceptor serves the sync members and is quietly absent from the async
    /// one, which is how an argument-rewriting interceptor stops rewriting halfway through a service.
    /// </summary>
    [Fact]
    public void InterceptorThatServesSomeMembers_ReportsDM0015ForTheRest() {
        var result = GeneratorTestHarness.Run(
            Intercepted(
                """
                public interface IMixed {
                    int Count(string key);
                    Task<int> CountAsync(string key);
                }

                [SingletonService]
                [Intercept(typeof(SyncOnlyInterceptor))]
                public class Mixed : IMixed {
                    public int Count(string key) => key.Length;
                    public Task<int> CountAsync(string key) => Task.FromResult(key.Length);
                }
                """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0015");

        Assert.Contains("CountAsync", diagnostic.GetMessage());
        Assert.DoesNotContain("'Count'", diagnostic.GetMessage());
    }

    /// <summary>
    /// An interceptor covering every shape the service uses says nothing.
    /// </summary>
    [Fact]
    public void InterceptorThatServesEveryMember_DoesNotReportDM0015() {
        var result = GeneratorTestHarness.Run(
            Intercepted(
                """
                public interface ISyncOnly {
                    int Count(string key);
                }

                [SingletonService]
                [Intercept(typeof(SyncOnlyInterceptor))]
                public class SyncOnly : ISyncOnly {
                    public int Count(string key) => key.Length;
                }
                """));

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0015");
    }

    /// <summary>
    /// DM0008 drops the whole wrapper, not only the member it names, and the message has to say so —
    /// the guide read as though the other members were still intercepted.
    /// </summary>
    [Fact]
    public void UnsupportedMember_ReportsThatNoMemberIsIntercepted() {
        var result = GeneratorTestHarness.Run(
            Intercepted(
                """
                public interface IAwkward {
                    bool TryGet(string key, out string value);
                    int Fine(string key);
                }

                [SingletonService]
                [Intercept(typeof(SyncOnlyInterceptor))]
                public class Awkward : IAwkward {
                    public bool TryGet(string key, out string value) { value = key; return true; }
                    public int Fine(string key) => key.Length;
                }
                """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0008");

        Assert.Contains("none of its members are intercepted", diagnostic.GetMessage());
        Assert.Contains("TryGet", diagnostic.GetMessage());
    }

    private static string Intercepted(string body) =>
        $$"""
          using System.Threading.Tasks;
          using DependencyModules.Runtime.Attributes;
          using DependencyModules.Runtime.Interception;

          namespace TestNamespace;

          [SingletonService]
          public class SyncOnlyInterceptor : IInterceptor {
              public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
          }

          {{body}}

          [DependencyModule]
          public partial class TestModule;
          """;

    private static string OpenGenericStore(string body, string moduleAttributes = "") =>
        $$"""
          using DependencyModules.Runtime.Attributes;

          namespace TestNamespace;

          public interface IStore<T> { string Read(T key); }

          [SingletonService]
          public class Store<T> : IStore<T> { public string Read(T key) => "store"; }

          {{body}}

          [DependencyModule]
          {{moduleAttributes}}
          public partial class TestModule;
          """;

    private static string CrossWiredLedger(string implementation) =>
        $$"""
          using DependencyModules.Runtime.Attributes;

          namespace TestNamespace;

          public interface ILedger<T>;
          public interface IAudit<T>;

          [CrossWireService]
          {{implementation}}

          [DependencyModule]
          public partial class TestModule;
          """;

    private static string Module(string body) =>
        $$"""
          using DependencyModules.Runtime.Attributes;

          namespace TestNamespace;

          public interface IThing;

          {{body}}

          [DependencyModule]
          public partial class TestModule;
          """;
}

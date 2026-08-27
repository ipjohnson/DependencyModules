using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Whether a DM diagnostic can be silenced where it is written.
///
/// Roslyn decides both <c>.editorconfig</c> severity and <c>#pragma warning disable</c> from the
/// diagnostic's syntax tree. A location built from a file path alone has none — it is an
/// <see cref="LocationKind.ExternalFile"/> location, and <c>SourceTree</c> is null — so neither
/// mechanism can reach it and only the compilation-wide <c>NoWarn</c> works.
///
/// That is what happened here, and it happened *because* of a fix: 1.1.0 moved ten diagnostics off
/// the project and onto the declaration they are about, using a location rebuilt from primitives
/// because a model cannot hold a SyntaxTree without pinning it and defeating the incremental cache.
/// The same release documented that <c>.editorconfig</c> worked. It worked for exactly the two codes
/// that had never been moved.
///
/// Asserting on the tree rather than on filtered output is deliberate: the tree is the precondition
/// Roslyn actually keys off, and it is the thing that can regress. Nothing here exercised any
/// suppression mechanism before this file existed.
///
/// Every code that has a location is covered. DM0001 is the only one absent, and it is absent
/// because a generator that failed has nothing to point at. Adding a code without adding it here
/// means shipping one more diagnostic nobody can turn off where they wrote it.
/// </summary>
public class DiagnosticSuppressionTests {

    [Theory]
    [MemberData(nameof(Triggers))]
    public void ADiagnostic_IsReportedAgainstASyntaxTree(string code, string source) {
        var result = GeneratorTestHarness.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == code);

        Assert.True(diagnostic.Location.SourceTree != null,
            $"{code} was reported at {diagnostic.Location.Kind} with no syntax tree, so neither " +
            ".editorconfig nor #pragma can silence it. Location: " + diagnostic.Location);
    }

    [Theory]
    [MemberData(nameof(Triggers))]
    public void ADiagnostic_IsReportedInTheFileThatCausedIt(string code, string source) {
        var result = GeneratorTestHarness.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == code);

        Assert.Equal("Test.cs", System.IO.Path.GetFileName(diagnostic.Location.GetLineSpan().Path));
    }

    /// <summary>
    /// One trigger per code, kept minimal. DM0001 is deliberately absent: it reports a generator
    /// that failed, where there is genuinely nothing to point at. DM0016 and DM0019 are absent
    /// because they are the two that were never moved and already carry a real location — they are
    /// covered by AssemblyModuleAttributeDiagnosticsTests.
    /// </summary>
    public static IEnumerable<object[]> Triggers() {
        yield return ["DM0002", Module("""
                                       public interface IThing;
                                       [SingletonService]
                                       public abstract class Thing : IThing;
                                       """)];

        yield return ["DM0003", """
                                using DependencyModules.Runtime.Attributes;
                                namespace TestNamespace;
                                [DependencyModule]
                                public class NotPartialModule;
                                """];

        yield return ["DM0012", Module("""
                                       public interface IThing;
                                       [SingletonService]
                                       [IfEnvironment]
                                       public class Thing : IThing;
                                       """)];

        yield return ["DM0014", Module("""
                                       public interface IThing<T>;
                                       [CrossWireService]
                                       public class Thing<T> : IThing<T>;
                                       """)];

        yield return ["DM0017", """
                                using DependencyModules.Runtime.Attributes;
                                namespace TestNamespace;
                                public static class Outer {
                                    [DependencyModule]
                                    public partial class NestedModule;
                                }
                                """];

        yield return ["DM0004", Convention("""
                                           public interface IFoo { }
                                           public class Foo : IFoo { }

                                           [DependencyModule]
                                           public partial class TestModule : IConventionModule {
                                               void IConventionModule.Conventions(IConventionDefinitions conventions) {
                                                   conventions.RegisterAll<IFoo>().AsSingleton();
                                                   conventions.RegisterAll<IFoo>().AsScoped();
                                               }
                                           }
                                           """)];

        yield return ["DM0005", Convention("""
                                           [DependencyModule]
                                           public partial class TestModule : IConventionModule {
                                               void IConventionModule.Conventions(IConventionDefinitions conventions) {
                                                   conventions.RegisterAll().WithName("NothingMatchesThis").AsSelf().AsScoped();
                                               }
                                           }
                                           """)];

        yield return ["DM0006", Convention("""
                                           public interface IFoo { }
                                           public class Foo : IFoo { private Foo() { } }

                                           [DependencyModule]
                                           public partial class TestModule : IConventionModule {
                                               void IConventionModule.Conventions(IConventionDefinitions conventions) {
                                                   conventions.RegisterAll<IFoo>().AsSingleton();
                                               }
                                           }
                                           """)];

        yield return ["DM0009", Convention("""
                                           public interface IFoo { }
                                           public class Foo : IFoo { }

                                           [DependencyModule]
                                           public partial class TestModule : IConventionModule {
                                               void IConventionModule.Conventions(IConventionDefinitions conventions) {
                                                   conventions.RegisterAll<IFoo>().AsSelf().AlsoAsSelf().AsSingleton();
                                               }
                                           }
                                           """)];

        yield return ["DM0010", Convention("""
                                           public interface IFoo { }
                                           public class Foo : IFoo { }

                                           [DependencyModule]
                                           public partial class TestModule : IConventionModule {
                                               void IConventionModule.Conventions(IConventionDefinitions conventions) {
                                                   conventions.RegisterAll<IFoo>().AsSingleton();
                                               }
                                           }
                                           """)];

        yield return ["DM0011", Module("""
                                       public interface IThing;
                                       [SingletonService]
                                       [IfEnvironment("Development")]
                                       public class Thing : IThing;
                                       """)];

        yield return ["DM0007", """
                                using DependencyModules.Runtime.Attributes;

                                namespace TestNamespace;

                                public interface IThing { string Read(); }

                                [SingletonService]
                                public class Thing : IThing {
                                    public string Read() => "";
                                }

                                [Decorator(Order = 1)]
                                public class FirstDecorator(IThing inner) : IThing {
                                    public string Read() => inner.Read();
                                }

                                [Decorator(Order = 1)]
                                public class SecondDecorator(IThing inner) : IThing {
                                    public string Read() => inner.Read();
                                }

                                [DependencyModule]
                                public partial class TestModule;
                                """];

        yield return ["DM0013", """
                                using DependencyModules.Runtime.Attributes;

                                namespace TestNamespace;

                                public interface IStore<T> { string Read(T key); }

                                [SingletonService]
                                public class Store<T> : IStore<T> {
                                    public string Read(T key) => "";
                                }

                                [Decorator]
                                public class LoggingStore<T>(IStore<T> inner) : IStore<T> {
                                    public string Read(T key) => inner.Read(key);
                                }

                                [DependencyModule]
                                public partial class TestModule;
                                """];

        yield return ["DM0008", Intercepted("""
                                            public interface IAwkward {
                                                bool TryGet(string key, out string value);
                                            }

                                            [SingletonService]
                                            [Intercept(typeof(SyncOnlyInterceptor))]
                                            public class Awkward : IAwkward {
                                                public bool TryGet(string key, out string value) { value = key; return true; }
                                            }
                                            """)];

        yield return ["DM0015", Intercepted("""
                                            public interface IAsyncOnly {
                                                Task<string> GetAsync(string key);
                                            }

                                            [SingletonService]
                                            [Intercept(typeof(SyncOnlyInterceptor))]
                                            public class AsyncOnly : IAsyncOnly {
                                                public Task<string> GetAsync(string key) => Task.FromResult(key);
                                            }
                                            """)];

        yield return ["DM0018", """
                                using DependencyModules.Runtime.Attributes;
                                namespace TestNamespace;
                                [DependencyModule]
                                public partial class TestModule {
                                    public int SizeLimit { get; set; }
                                }
                                """];
    }

    private static string Convention(string body) =>
        $$"""
          using System;
          using DependencyModules.Runtime.Attributes;
          using DependencyModules.Runtime.Conventions;

          namespace TestNamespace;

          {{body}}
          """;

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

    private static string Module(string body) =>
        $$"""
          using DependencyModules.Runtime.Attributes;

          namespace TestNamespace;

          [DependencyModule]
          public partial class TestModule;

          {{body}}
          """;
}

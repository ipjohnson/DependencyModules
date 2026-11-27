using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The <c>Into*</c> attributes. These tests build the generated code and read the collections
/// from a service provider, because the order, the keys and the time an item is built are all
/// decided when the modules load or later.
/// </summary>
public class GlueCollectionTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        public interface IClock { }

        [SingletonService]
        public class Clock : IClock { }

        public enum Stage
        {
            Auth,
            Routing,
            Endpoint,
        }

        public abstract class Handler
        {
            public static int Built;
            public static List<string> Disposed = new();

            protected Handler()
            {
                Built++;
            }

            public string Name => GetType().Name;
        }

        public class ListProbe(IReadOnlyList<Handler> handlers)
        {
            public int Count => handlers.Count;

            public Handler Get(int index) => handlers[index];

            public string Names => string.Join(",", handlers.Select(handler => handler.Name));
        }

        public class MapProbe(IReadOnlyDictionary<string, Handler> handlers)
        {
            public int Count => handlers.Count;

            public string Keys => string.Join(",", handlers.Keys);

            public Handler Get(string key) => handlers[key];
        }

        public class SetProbe(IReadOnlySet<Handler> handlers)
        {
            public int Count => handlers.Count;

            public string Names =>
                string.Join(",", handlers.Select(handler => handler.Name).OrderBy(name => name));
        }

        """;

    private static GeneratedAssembly Compile(
        string source,
        IModuleEnvironment? environment = null
    ) => GeneratedAssembly.Create(Preamble + source, environment: environment);

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    private static int Built(GeneratedAssembly assembly) =>
        (int)assembly.Type("Handler").GetField("Built")!.GetValue(null)!;

    private static dynamic Probe(
        GeneratedAssembly assembly,
        IServiceProvider provider,
        string name
    ) => ActivatorUtilities.CreateInstance(provider, assembly.Type(name));

    [Fact]
    public void AMap_GivesTheItemOfAKey_AndBuildsOnlyThatItem()
    {
        var assembly = Compile(
            """
            [IntoMap<string, Handler>("tools/call")]
            public class CallHandler(IClock clock) : Handler
            {
                public IClock Clock => clock;
            }

            [IntoMap<string, Handler>("tools/list")]
            public class ListHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        var probe = Probe(assembly, provider, "MapProbe");

        Assert.Equal(0, Built(assembly));
        Assert.Equal("tools/call,tools/list", (string)probe.Keys);
        Assert.Equal(2, (int)probe.Count);
        Assert.Equal(0, Built(assembly));

        dynamic handler = probe.Get("tools/call");
        Assert.Equal("CallHandler", (string)handler.Name);
        Assert.Equal(1, Built(assembly));

        // The item is kept, and its constructor parameter came from the service provider.
        Assert.Same((object)handler, (object)probe.Get("tools/call"));
        Assert.Equal(1, Built(assembly));
        Assert.Same(provider.GetRequiredService(assembly.Type("IClock")), (object)handler.Clock);
    }

    [Fact]
    public void TheItemsOfACollection_AreNotServices()
    {
        var assembly = Compile(
            """
            [IntoMap<string, Handler>("a")]
            [IntoSet<Handler>]
            [IntoList<Handler>]
            public class OnlyHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Empty(provider.GetServices(assembly.Type("Handler")));
        Assert.Null(provider.GetService(assembly.Type("OnlyHandler")));
        Assert.Equal("OnlyHandler", (string)Probe(assembly, provider, "ListProbe").Names);
        Assert.Equal("OnlyHandler", (string)Probe(assembly, provider, "SetProbe").Names);
        Assert.Equal("a", (string)Probe(assembly, provider, "MapProbe").Keys);
    }

    [Fact]
    public void AMap_HasEnumAndNumberKeys()
    {
        var assembly = Compile(
            """
            [IntoMap<Stage, Handler>(Stage.Routing)]
            [IntoMap<int, Handler>(7)]
            public class RoutingHandler : Handler { }

            [IntoMap<Stage, Handler>(Stage.Auth)]
            public class AuthHandler : Handler { }

            public class StageProbe(
                IReadOnlyDictionary<Stage, Handler> byStage,
                IReadOnlyDictionary<int, Handler> byNumber
            )
            {
                public string Stages => string.Join(",", byStage.Keys);

                public string Routing => byStage[Stage.Routing].Name;

                public string Seven => byNumber[7].Name;

                public bool HasEight => byNumber.ContainsKey(8);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        var probe = Probe(assembly, provider, "StageProbe");

        Assert.Equal("Auth,Routing", (string)probe.Stages);
        Assert.Equal("RoutingHandler", (string)probe.Routing);
        Assert.Equal("RoutingHandler", (string)probe.Seven);
        Assert.False((bool)probe.HasEight);
    }

    [Fact]
    public void ASet_HasOneInstanceOfEachItem()
    {
        var assembly = Compile(
            """
            [IntoSet<Handler>]
            public class FirstHandler : Handler { }

            [IntoSet<Handler>]
            public class SecondHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        var probe = Probe(assembly, provider, "SetProbe");

        Assert.Equal(0, Built(assembly));
        Assert.Equal("FirstHandler,SecondHandler", (string)probe.Names);
        Assert.Equal(2, (int)probe.Count);
        Assert.Equal(2, Built(assembly));
    }

    [Fact]
    public void AListWithTypeOrder_PutsAnItemAfterTheTypesItDependsOn()
    {
        var assembly = Compile(
            """
            [IntoList<Handler>(typeof(RoutingHandler))]
            public class AHandler : Handler { }

            [IntoList<Handler>(typeof(AuthHandler))]
            public class RoutingHandler : Handler { }

            [IntoList<Handler>]
            public class ZHandler : Handler { }

            [IntoList<Handler>]
            public class AuthHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        var probe = Probe(assembly, provider, "ListProbe");

        Assert.Equal(0, Built(assembly));
        Assert.Equal(4, (int)probe.Count);
        Assert.Equal(0, Built(assembly));

        // AuthHandler and ZHandler have no order between them, so their names give it. AHandler
        // is last because it is after RoutingHandler.
        Assert.Equal("AuthHandler,RoutingHandler,AHandler,ZHandler", (string)probe.Names);
    }

    [Fact]
    public void AListWithTypeOrder_PutsAnItemAfterEachItemOfABaseType()
    {
        var assembly = Compile(
            """
            public abstract class SecurityHandler : Handler { }

            [IntoList<Handler>(typeof(SecurityHandler), typeof(IClock))]
            public class AHandler : Handler { }

            [IntoList<Handler>]
            public class TokenHandler : SecurityHandler { }

            [IntoList<Handler>]
            public class CorsHandler : SecurityHandler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal(
            "CorsHandler,TokenHandler,AHandler",
            (string)Probe(assembly, provider, "ListProbe").Names
        );
    }

    [Fact]
    public void AListWithOrderValues_SortsByValue_AndEqualValuesAreOneLevel()
    {
        var assembly = Compile(
            """
            [IntoList<Handler, int>(20)]
            public class AHandler : Handler { }

            [IntoList<Handler, int>(10)]
            public class ZHandler : Handler { }

            [IntoList<Handler, int>(10)]
            public class BHandler : Handler { }

            [IntoList<Handler, int>(-5)]
            public class MHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal(
            "MHandler,BHandler,ZHandler,AHandler",
            (string)Probe(assembly, provider, "ListProbe").Names
        );
    }

    [Fact]
    public void AListWithOrderValues_PutsAnItemAfterTheValuesItDependsOn()
    {
        var assembly = Compile(
            """
            [IntoList<Handler, string>("auth", "routing")]
            public class AuthHandler : Handler { }

            [IntoList<Handler, string>("routing")]
            public class RoutingHandler : Handler { }

            [IntoList<Handler, string>("routing")]
            public class OtherRoutingHandler : Handler { }

            [IntoList<Handler, string>("cors", "missing")]
            public class CorsHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        // "auth" sorts first by value, but it is after the two "routing" items. A value with no
        // item has no effect.
        Assert.Equal(
            "CorsHandler,OtherRoutingHandler,RoutingHandler,AuthHandler",
            (string)Probe(assembly, provider, "ListProbe").Names
        );
    }

    [Fact]
    public void AListWithEnumOrderValues_SortsByTheEnumValue()
    {
        var assembly = Compile(
            """
            [IntoList<Handler, Stage>(Stage.Endpoint)]
            public class AHandler : Handler { }

            [IntoList<Handler, Stage>(Stage.Auth, Stage.Routing)]
            public class BHandler : Handler { }

            [IntoList<Handler, Stage>(Stage.Routing)]
            public class CHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal(
            "CHandler,BHandler,AHandler",
            (string)Probe(assembly, provider, "ListProbe").Names
        );
    }

    [Fact]
    public void AListWithAnOrderCycle_FailsTheLoad_AndNamesTheItems()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Compile(
                """
                [IntoList<Handler>(typeof(SecondHandler))]
                public class FirstHandler : Handler { }

                [IntoList<Handler>(typeof(FirstHandler))]
                public class SecondHandler : Handler { }

                [IntoList<Handler>]
                public class ThirdHandler : Handler { }

                [DependencyModule]
                public partial class TestModule;
                """
            )
        );

        Assert.Contains("order cycle", exception.Message);
        Assert.Contains(
            "TestNamespace.FirstHandler, TestNamespace.SecondHandler",
            exception.Message
        );
        Assert.DoesNotContain("ThirdHandler", exception.Message);
    }

    [Fact]
    public void AnItem_IsBuiltOneTimeForEachCollectionInstance_AndDisposedWithItsScope()
    {
        var assembly = Compile(
            """
            [IntoList<Handler>]
            public class DisposableHandler : Handler, IDisposable
            {
                public void Dispose() => Disposed.Add(Name);
            }

            [IntoList<Handler>]
            public class AsyncHandler : Handler, IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    Disposed.Add(Name);
                    return default;
                }
            }

            [IntoList<Handler>]
            public class NeverReadHandler : Handler, IDisposable
            {
                public void Dispose() => Disposed.Add(Name);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var disposed = (List<string>)assembly.Type("Handler").GetField("Disposed")!.GetValue(null)!;

        using var provider = assembly.BuildProvider();

        using (var scope = provider.CreateScope())
        {
            var list = Probe(assembly, scope.ServiceProvider, "ListProbe");

            Assert.Same((object)list.Get(0), (object)list.Get(0));
            Assert.Equal("DisposableHandler", (string)list.Get(1).Name);
            Assert.Equal(2, Built(assembly));
            Assert.Empty(disposed);
        }

        Assert.Equal(new[] { "DisposableHandler", "AsyncHandler" }, disposed);

        // A second scope gets a new collection with new items.
        using (var scope = provider.CreateScope())
        {
            _ = Probe(assembly, scope.ServiceProvider, "ListProbe").Get(0);
        }

        Assert.Equal(3, Built(assembly));
    }

    [Fact]
    public void AnItemWithARealm_IsOnlyInTheCollectionOfThatModule()
    {
        var assembly = Compile(
            """
            [IntoList<Handler>]
            public class SharedHandler : Handler { }

            [IntoList<Handler>(Realm = typeof(OtherModule))]
            public class OtherHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class OtherModule;
            """
        );

        using (var provider = assembly.BuildProvider())
        {
            Assert.Equal("SharedHandler", (string)Probe(assembly, provider, "ListProbe").Names);
        }

        // A second load on the same service collection adds to the same list.
        assembly.Services.AddModule(
            (IDependencyModule)Activator.CreateInstance(assembly.Type("OtherModule"))!
        );

        using (var provider = assembly.BuildProvider())
        {
            Assert.Equal(
                "OtherHandler,SharedHandler",
                (string)Probe(assembly, provider, "ListProbe").Names
            );
        }
    }

    [Fact]
    public void TwoModulesOfOneProject_AddAnItemOneTime()
    {
        var assembly = Compile(
            """
            [IntoList<Handler, int>(1)]
            [IntoSet<Handler>]
            [IntoMap<string, Handler>("only")]
            public class OnlyHandler : Handler { }

            [DependencyModule]
            [SecondModule]
            public partial class TestModule;

            [DependencyModule]
            public partial class SecondModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal("OnlyHandler", (string)Probe(assembly, provider, "ListProbe").Names);
        Assert.Equal("OnlyHandler", (string)Probe(assembly, provider, "SetProbe").Names);
        Assert.Equal("only", (string)Probe(assembly, provider, "MapProbe").Keys);
    }

    [Fact]
    public void AnItemWithAnEnvironmentCondition_IsInTheCollectionOnlyInThatEnvironment()
    {
        const string source = """
            [IntoList<Handler>]
            public class AlwaysHandler : Handler { }

            [IntoList<Handler>]
            [IfEnvironment("Development")]
            public class DevelopmentHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """;

        var production = Compile(source, new ModuleEnvironment(false, "Production"));
        var development = Compile(source, new ModuleEnvironment(false, "Development"));

        using var productionProvider = production.BuildProvider();
        using var developmentProvider = development.BuildProvider();

        Assert.Equal(
            "AlwaysHandler",
            (string)Probe(production, productionProvider, "ListProbe").Names
        );
        Assert.Equal(
            "AlwaysHandler,DevelopmentHandler",
            (string)Probe(development, developmentProvider, "ListProbe").Names
        );
    }

    [Fact]
    public void ACollectionThatAModuleRegisters_IsKept()
    {
        var assembly = Compile(
            """
            [IntoList<Handler>]
            public class GlueHandler : Handler { }

            public class ManualHandler : Handler { }

            [DependencyModule]
            public partial class TestModule : IServiceCollectionConfiguration
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<IReadOnlyList<Handler>>(new Handler[] { new ManualHandler() });
                }
            }
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal("ManualHandler", (string)Probe(assembly, provider, "ListProbe").Names);
        Assert.Single(
            assembly.Services,
            descriptor =>
                descriptor.ServiceType
                == typeof(IReadOnlyList<>).MakeGenericType(assembly.Type("Handler"))
        );
    }

    [Fact]
    public void TwoModulesWithOneKey_FailTheLoad_AndNameTheClasses()
    {
        var assembly = Compile(
            """
            [IntoMap<string, Handler>("same", Realm = typeof(TestModule))]
            public class FirstHandler : Handler { }

            [IntoMap<string, Handler>("same", Realm = typeof(OtherModule))]
            public class SecondHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class OtherModule;
            """
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            assembly.Services.AddModule(
                (IDependencyModule)Activator.CreateInstance(assembly.Type("OtherModule"))!
            )
        );

        Assert.Contains("'same'", exception.Message);
        Assert.Contains("TestNamespace.FirstHandler", exception.Message);
        Assert.Contains("TestNamespace.SecondHandler", exception.Message);
    }

    [Fact]
    public void TwoClassesOfOneModuleWithOneKey_AreDM0028()
    {
        var result = Run(
            """
            [IntoMap<string, Handler>("same")]
            public class FirstHandler : Handler { }

            [IntoMap<string, Handler>("same")]
            public class SecondHandler : Handler { }

            [IntoMap<string, Handler>("other")]
            public class ThirdHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0028");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'FirstHandler' and 'SecondHandler'", diagnostic.GetMessage());
        Assert.Contains("\"same\"", diagnostic.GetMessage());
    }

    [Fact]
    public void AListWithTwoOrderSchemes_IsDM0029()
    {
        var result = Run(
            """
            [IntoList<Handler>]
            public class FirstHandler : Handler { }

            [IntoList<Handler, int>(1)]
            public class SecondHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0029");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("IntoList<T, int>", diagnostic.GetMessage());
        Assert.Contains("IntoList<T>", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("public abstract class BadHandler : Handler { }", "it is abstract")]
    [InlineData("public class BadHandler { }", "it is not assignable to 'TestNamespace.Handler'")]
    [InlineData(
        "public class BadHandler : Handler { private BadHandler() { } }",
        "it has no constructor that the generated module can call"
    )]
    [InlineData(
        "public class BadHandler(int retries) : Handler { }",
        "its constructor parameter 'retries' has the type 'int'"
    )]
    [InlineData("public class BadHandler<T> : Handler { }", "it is an open generic class")]
    public void AClassThatCannotBeAnItem_IsDM0027_AndTheOtherItemsStay(
        string declaration,
        string reason
    )
    {
        var source =
            "[IntoList<Handler>]\n"
            + declaration
            + """

                [IntoList<Handler>]
                public class GoodHandler : Handler { }

                [DependencyModule]
                public partial class TestModule;
                """;

        var result = Run(source);

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0027");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("the list of TestNamespace.Handler", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());

        var assembly = Compile(source);
        using var provider = assembly.BuildProvider();

        Assert.Equal("GoodHandler", (string)Probe(assembly, provider, "ListProbe").Names);
    }

    [Fact]
    public void AMap_GivesItsValuesAndPairs_AndHasNoItemForAMissingKey()
    {
        var assembly = Compile(
            """
            [IntoMap<string, Handler>("b")]
            public class BHandler : Handler { }

            [IntoMap<string, Handler>("a")]
            public class AHandler : Handler { }

            public class FullMapProbe(IReadOnlyDictionary<string, Handler> handlers)
            {
                public string Values => string.Join(",", handlers.Values.Select(handler => handler.Name));

                public string Pairs =>
                    string.Join(",", handlers.Select(pair => pair.Key + "=" + pair.Value.Name));

                public bool HasMissing => handlers.TryGetValue("missing", out _);

                public Handler Missing => handlers["missing"];
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        var probe = Probe(assembly, provider, "FullMapProbe");

        Assert.Equal("AHandler,BHandler", (string)probe.Values);
        Assert.Equal("a=AHandler,b=BHandler", (string)probe.Pairs);
        Assert.False((bool)probe.HasMissing);
        Assert.Equal(2, Built(assembly));

        var exception = Assert.Throws<KeyNotFoundException>(() => (object)probe.Missing);

        Assert.Contains("'missing'", exception.Message);
    }

    [Fact]
    public void ASet_AnswersTheSetQuestions()
    {
        var assembly = Compile(
            """
            [IntoSet<Handler>]
            public class FirstHandler : Handler { }

            [IntoSet<Handler>]
            public class SecondHandler : Handler { }

            public class FullSetProbe(IReadOnlySet<Handler> handlers)
            {
                public bool Answers()
                {
                    var all = handlers.ToList();
                    var one = all.Take(1).ToList();
                    var other = new List<Handler> { new FirstHandler() };

                    return handlers.Contains(all[0])
                        && !handlers.Contains(other[0])
                        && handlers.SetEquals(all)
                        && handlers.IsSubsetOf(all)
                        && handlers.IsSupersetOf(one)
                        && handlers.IsProperSupersetOf(one)
                        && !handlers.IsProperSubsetOf(all)
                        && handlers.Overlaps(one)
                        && !handlers.Overlaps(other);
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.True((bool)Probe(assembly, provider, "FullSetProbe").Answers());
    }

    [Fact]
    public async Task AnAsyncScope_DisposesTheItemsOfItsCollections()
    {
        var assembly = Compile(
            """
            [IntoSet<Handler>]
            public class AsyncHandler : Handler, IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    Disposed.Add(Name);
                    return default;
                }
            }

            [IntoMap<string, Handler>("sync")]
            public class SyncHandler : Handler, IDisposable
            {
                public void Dispose() => Disposed.Add(Name);
            }

            public class BothProbe(
                IReadOnlySet<Handler> set,
                IReadOnlyDictionary<string, Handler> map,
                IReadOnlyList<Handler>? list = null
            )
            {
                public int Read() => set.Count + (map["sync"] != null ? 1 : 0) + (list?.Count ?? 0);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var disposed = (List<string>)assembly.Type("Handler").GetField("Disposed")!.GetValue(null)!;

        await using var provider = assembly.BuildProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(2, (int)Probe(assembly, scope.ServiceProvider, "BothProbe").Read());
            Assert.Empty(disposed);
        }

        Assert.Equal(new[] { "AsyncHandler", "SyncHandler" }, disposed.OrderBy(name => name));
    }

    [Fact]
    public void TwoModulesWithTwoOrderSchemes_FailTheLoad_AndNameTheClass()
    {
        var assembly = Compile(
            """
            [IntoList<Handler>(Realm = typeof(TestModule))]
            public class FirstHandler : Handler { }

            [IntoList<Handler, Stage>(Stage.Auth, Realm = typeof(OtherModule))]
            public class SecondHandler : Handler { }

            [DependencyModule]
            public partial class TestModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class OtherModule;
            """
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            assembly.Services.AddModule(
                (IDependencyModule)Activator.CreateInstance(assembly.Type("OtherModule"))!
            )
        );

        Assert.Contains(
            "System.Collections.Generic.IReadOnlyList<TestNamespace.Handler>",
            exception.Message
        );
        Assert.Contains("TestNamespace.SecondHandler uses IntoList<T, Stage>", exception.Message);
        Assert.Contains("IntoList<T>", exception.Message);
    }

    [Fact]
    public void AListWithOrderValuesAndACycle_FailsTheLoad()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Compile(
                """
                [IntoList<Handler, string>("a", "b")]
                public class FirstHandler : Handler { }

                [IntoList<Handler, string>("b", "a")]
                public class SecondHandler : Handler { }

                [DependencyModule]
                public partial class TestModule;
                """
            )
        );

        Assert.Contains("order cycle", exception.Message);
    }

    [Fact]
    public void AModuleWithNoItems_GetsNoGlueFile()
    {
        var result = Run(
            """
            [DependencyModule]
            public partial class TestModule;
            """
        );

        Assert.DoesNotContain(result.GeneratedSources.Keys, name => name.Contains("Glue"));
    }

    [Fact]
    public void ARecordModule_CompilesWithItems()
    {
        var assembly = Compile(
            """
            [IntoSet<Handler>]
            public class OnlyHandler : Handler { }

            [DependencyModule]
            public partial record TestModule;
            """
        );

        using var provider = assembly.BuildProvider();

        Assert.Equal("OnlyHandler", (string)Probe(assembly, provider, "SetProbe").Names);
    }
}

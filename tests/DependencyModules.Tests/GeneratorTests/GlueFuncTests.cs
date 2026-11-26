using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The registrations that the generator writes for <c>Func</c> constructor parameters, and the
/// rules that give a constructor parameter of an object that the generated code builds.
/// </summary>
public class GlueFuncTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        public interface IClock { }

        [SingletonService]
        public class Clock : IClock { }

        public readonly record struct OrderId(int Value);

        """;

    private static GeneratedAssembly Compile(string source) =>
        GeneratedAssembly.Create(Preamble + source);

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    private static string Glue(GeneratorResult result) => result.SourceContaining("Glue");

    [Fact]
    public void AFuncWithACallerArgument_BuildsTheClass_FromTheArgumentAndTheProvider()
    {
        var assembly = Compile(
            """
            public class OrderProcessor(OrderId id, IClock clock)
            {
                public OrderId Id => id;

                public IClock Clock => clock;
            }

            [SingletonService]
            public class Checkout(Func<OrderId, OrderProcessor> createProcessor)
            {
                public OrderProcessor Create(int id) => createProcessor(new OrderId(id));
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic checkout = provider.GetRequiredService(assembly.Type("Checkout"));

        dynamic first = checkout.Create(7);
        dynamic second = checkout.Create(8);

        Assert.Equal(7, (int)first.Id.Value);
        Assert.Equal(8, (int)second.Id.Value);
        Assert.NotSame((object)first, (object)second);
        Assert.Same(provider.GetRequiredService(assembly.Type("IClock")), (object)first.Clock);

        // The class that the delegate builds has no registration.
        Assert.Null(provider.GetService(assembly.Type("OrderProcessor")));
    }

    [Fact]
    public void AFuncWithTwoCallerArguments_MatchesThemByType_InAnyOrder()
    {
        var assembly = Compile(
            """
            public class Report(IClock clock, string title, int pages)
            {
                public string Text => title + ":" + pages + ":" + (clock != null);
            }

            [SingletonService]
            public class Printer(Func<int, string, Report> createReport)
            {
                public string Print() => createReport(3, "sales").Text;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic printer = provider.GetRequiredService(assembly.Type("Printer"));

        Assert.Equal("sales:3:True", (string)printer.Print());
    }

    [Fact]
    public void AFuncWithCallerArguments_UsesTheConstructorThatTakesThem()
    {
        var assembly = Compile(
            """
            public class Report
            {
                public Report(IClock clock, IServiceProvider provider, IClock second)
                {
                    Text = "wrong";
                }

                public Report(string title)
                {
                    Text = title;
                }

                public string Text { get; }
            }

            [SingletonService]
            public class Printer(Func<string, Report> createReport)
            {
                public string Print() => createReport("sales").Text;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic printer = provider.GetRequiredService(assembly.Type("Printer"));

        Assert.Equal("sales", (string)printer.Print());
    }

    [Fact]
    public void AFuncWithNoArguments_OverAnInterface_GetsTheServiceFromTheProvider()
    {
        var assembly = Compile(
            """
            [SingletonService]
            public class Consumer(Func<IClock> clock)
            {
                public IClock Clock => clock();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

        Assert.Same(provider.GetRequiredService(assembly.Type("IClock")), (object)consumer.Clock);
    }

    [Fact]
    public void AFuncWithNoArguments_OverAConcreteClass_BuildsANewInstanceForEachCall()
    {
        var assembly = Compile(
            """
            public class Worker(IClock clock)
            {
                public IClock Clock => clock;
            }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker)
            {
                public Worker Create() => createWorker();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

        dynamic first = consumer.Create();

        Assert.NotSame((object)first, (object)consumer.Create());
        Assert.Same(provider.GetRequiredService(assembly.Type("IClock")), (object)first.Clock);
    }

    [Fact]
    public void AConcreteClassThatTheApplicationRegisters_ComesFromTheProvider()
    {
        var assembly = Compile(
            """
            public class Settings
            {
                public string Name { get; set; } = "new";
            }

            public class Worker(Settings settings)
            {
                public string Name => settings.Name;
            }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker, Func<Settings> settings)
            {
                public string WorkerName => createWorker().Name;

                public string SettingsName => settings().Name;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using (var provider = assembly.BuildProvider())
        {
            dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

            Assert.Equal("new", (string)consumer.WorkerName);
            Assert.Equal("new", (string)consumer.SettingsName);
        }

        // Added after the modules loaded, as code after AddModule in Program.cs does.
        dynamic settings = Activator.CreateInstance(assembly.Type("Settings"))!;
        settings.Name = "registered";
        assembly.Services.AddSingleton(assembly.Type("Settings"), (object)settings);

        using (var provider = assembly.BuildProvider())
        {
            dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

            Assert.Equal("registered", (string)consumer.WorkerName);
            Assert.Equal("registered", (string)consumer.SettingsName);
        }
    }

    [Fact]
    public void AClassWithAServiceAttribute_IsNotBuiltWithNew()
    {
        var result = Run(
            """
            public interface IStore { }

            [SingletonService]
            public class Store : IStore { }

            public class Worker(Store store, IStore service) { }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var glue = Glue(result);

        Assert.DoesNotContain("new global::TestNamespace.Store", glue);
        Assert.Contains("provider.GetRequiredService<global::TestNamespace.Store>()", glue);
        Assert.Contains("provider.GetRequiredService<global::TestNamespace.IStore>()", glue);
    }

    [Fact]
    public void AFrameworkClassAndAClassWithAValueParameter_ComeFromTheProvider()
    {
        var result = Run(
            """
            public class Rates(decimal rate) { }

            public class Worker(System.Text.StringBuilder text, Rates rates, IClock? clock) { }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var glue = Glue(result);

        Assert.Contains("provider.GetRequiredService<global::System.Text.StringBuilder>()", glue);
        Assert.Contains("provider.GetRequiredService<global::TestNamespace.Rates>()", glue);
        Assert.Contains("provider.GetService<global::TestNamespace.IClock>()", glue);
        Assert.DoesNotContain("new global::System.Text.StringBuilder", glue);
        Assert.DoesNotContain("new global::TestNamespace.Rates", glue);
    }

    [Fact]
    public void AKeyedParameterAndTheProvider_AreGivenToAnObjectThatTheGlueBuilds()
    {
        var assembly = Compile(
            """
            [SingletonService(Key = "utc")]
            public class UtcClock : IClock { }

            public class Worker([FromKeyedServices("utc")] IClock clock, IServiceProvider provider)
            {
                public IClock Clock => clock;

                public IServiceProvider Provider => provider;
            }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker)
            {
                public Worker Create() => createWorker();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));
        dynamic worker = consumer.Create();

        Assert.Same(
            provider.GetRequiredKeyedService(assembly.Type("IClock"), "utc"),
            (object)worker.Clock
        );
        Assert.NotNull((object)worker.Provider);
    }

    [Fact]
    public void AFuncThatTheApplicationRegisters_IsKept()
    {
        var assembly = Compile(
            """
            public class Worker(string name)
            {
                public string Name => name;
            }

            [SingletonService]
            public class Consumer(Func<string, Worker> createWorker)
            {
                public string Name => createWorker("x").Name;
            }

            [DependencyModule]
            public partial class TestModule : IServiceCollectionConfiguration
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton<Func<string, Worker>>(name => new Worker("manual " + name));
                }
            }
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

        Assert.Equal("manual x", (string)consumer.Name);
        Assert.Single(
            assembly.Services,
            descriptor =>
                descriptor.ServiceType
                == typeof(Func<,>).MakeGenericType(typeof(string), assembly.Type("Worker"))
        );
    }

    [Fact]
    public void AFuncParameterOfACollectionItem_AndOfAnObjectThatTheGlueBuilds_IsRegistered()
    {
        var assembly = Compile(
            """
            public class Leaf(int depth)
            {
                public int Depth => depth;
            }

            public class Branch(Func<int, Leaf> createLeaf)
            {
                public int Depth => createLeaf(4).Depth;
            }

            [IntoSet<Tree>]
            public class Tree(Branch branch, Func<IClock> clock)
            {
                public int Depth => branch.Depth;

                public IClock Clock => clock();
            }

            public class Forest(IReadOnlySet<Tree> trees)
            {
                public Tree Tree => trees.Single();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic forest = ActivatorUtilities.CreateInstance(provider, assembly.Type("Forest"));

        Assert.Equal(4, (int)forest.Tree.Depth);
        Assert.Same(
            provider.GetRequiredService(assembly.Type("IClock")),
            (object)forest.Tree.Clock
        );
    }

    [Fact]
    public void AFuncParameterOfAFactoryMethod_IsRegistered()
    {
        var assembly = Compile(
            """
            public class Worker(string name)
            {
                public string Name => name;
            }

            public class Consumer(string name)
            {
                public string Name => name;
            }

            public static class Factories
            {
                [SingletonService]
                public static Consumer Create(Func<string, Worker> createWorker) =>
                    new Consumer(createWorker("made").Name);
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

        Assert.Equal("made", (string)consumer.Name);
    }

    [Theory]
    [InlineData("Func<Task>")]
    [InlineData("Func<int>")]
    [InlineData("Func<string, bool>")]
    [InlineData("Func<string, IClock>")]
    [InlineData("Func<string, System.Text.StringBuilder>")]
    [InlineData("Func<IServiceProvider, IClock>")]
    [InlineData("Action<string>")]
    public void ADelegateThatIsNotARequestForAService_GetsNoRegistration(string parameterType)
    {
        var result = Run(
            $$"""
            [SingletonService]
            public class Consumer({{parameterType}}? callback = null) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, name => name.Contains("Glue"));
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0030");
    }

    [Theory]
    [InlineData(
        "Func<string, int, Worker>",
        "its constructor has no parameter of the type 'int' for the caller argument"
    )]
    [InlineData("Func<string, string, Worker>", "two caller arguments have the type 'string'")]
    [InlineData("Func<IClock, Worker>", "its constructor parameter 'name' has the type 'string'")]
    public void AFuncWhoseArgumentsDoNotMatchTheConstructor_IsDM0030(
        string parameterType,
        string reason
    )
    {
        var result = Run(
            $$"""
            public class Worker(string name, IClock clock) { }

            [SingletonService]
            public class Consumer({{parameterType}} createWorker) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0030");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Consumer' takes a", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.DoesNotContain(result.GeneratedSources.Keys, name => name.Contains("Glue"));
    }

    [Fact]
    public void AFuncOfAServiceWithARealm_IsRegisteredByTheModuleOfThatRealm()
    {
        var result = Run(
            """
            public class Worker(string name) { }

            [SingletonService(Realm = typeof(OtherModule))]
            public class Consumer(Func<string, Worker> createWorker) { }

            [DependencyModule]
            public partial class TestModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class OtherModule;
            """
        );

        result.AssertNoErrors();

        var glueFile = Assert.Single(result.GeneratedSources.Keys, name => name.Contains("Glue"));

        Assert.Contains("OtherModule", glueFile);
    }

    [Fact]
    public void ObjectsThatNeedEachOther_DoNotStopTheGenerator()
    {
        var result = Run(
            """
            public class First(Second second) { }

            public class Second(First first) { }

            [SingletonService]
            public class Consumer(Func<First> createFirst) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        // The second use of First comes from the provider, which gives the error for the cycle.
        Assert.Contains("provider.GetRequiredService<global::TestNamespace.First>()", Glue(result));
    }

    [Fact]
    public void TheGlueFile_IsTheSameForTheSameSource()
    {
        const string source = """
            public class Worker(string name, IClock clock) { }

            public class Helper(IClock clock) { }

            [IntoMap<string, IClock>("b")]
            [IntoList<IClock, int>(2, 1)]
            public class BClock(Helper helper) : IClock { }

            [IntoMap<string, IClock>("a")]
            [IntoList<IClock, int>(1)]
            public class AClock : IClock { }

            [SingletonService]
            public class Consumer(Func<string, Worker> createWorker, Func<IClock> clock) { }

            [DependencyModule]
            public partial class TestModule;
            """;

        var result = Run(source);

        result.AssertNoErrors();
        Assert.Equal(Glue(result), Glue(Run(source)));

        Snapshot.Match(Glue(result));
    }
}

using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// The registrations that the generator writes for <c>Func</c> constructor parameters, and the
/// rules that give a constructor parameter of an object that the generated code builds.
/// </summary>
public partial class GlueFuncTests
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

    [VerifyTest]
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

        Verify((int)first.Id.Value == 7);
        Verify((int)second.Id.Value == 8);
        Verify(!ReferenceEquals((object)first, (object)second));
        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("IClock")),
                (object)first.Clock
            )
        );

        // The class that the delegate builds has no registration.
        Verify(provider.GetService(assembly.Type("OrderProcessor")) is null);
    }

    [VerifyTest]
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

        Verify((string)printer.Print() == "sales:3:True");
    }

    [VerifyTest]
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

        Verify((string)printer.Print() == "sales");
    }

    [VerifyTest]
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

        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("IClock")),
                (object)consumer.Clock
            )
        );
    }

    [VerifyTest]
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

        Verify(!ReferenceEquals((object)first, (object)consumer.Create()));
        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("IClock")),
                (object)first.Clock
            )
        );
    }

    [VerifyTest]
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

            Verify((string)consumer.WorkerName == "new");
            Verify((string)consumer.SettingsName == "new");
        }

        // Added after the modules loaded, as code after AddModule in Program.cs does.
        dynamic settings = Activator.CreateInstance(assembly.Type("Settings"))!;
        settings.Name = "registered";
        assembly.Services.AddSingleton(assembly.Type("Settings"), (object)settings);

        using (var provider = assembly.BuildProvider())
        {
            dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

            Verify((string)consumer.WorkerName == "registered");
            Verify((string)consumer.SettingsName == "registered");
        }
    }

    [VerifyTest]
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

        Verify(!glue.Contains("new global::TestNamespace.Store"));
        Verify(glue.Contains("provider.GetRequiredService<global::TestNamespace.Store>()"));
        Verify(glue.Contains("provider.GetRequiredService<global::TestNamespace.IStore>()"));
    }

    [VerifyTest]
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

        Verify(glue.Contains("provider.GetRequiredService<global::System.Text.StringBuilder>()"));
        Verify(glue.Contains("provider.GetRequiredService<global::TestNamespace.Rates>()"));
        Verify(glue.Contains("provider.GetService<global::TestNamespace.IClock>()"));
        Verify(!glue.Contains("new global::System.Text.StringBuilder"));
        Verify(!glue.Contains("new global::TestNamespace.Rates"));
    }

    [VerifyTest]
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

        Verify(
            ReferenceEquals(
                provider.GetRequiredKeyedService(assembly.Type("IClock"), "utc"),
                (object)worker.Clock
            )
        );
        Verify((object)worker.Provider is not null);
    }

    [VerifyTest]
    public void FromConstructor_BuildsTheParameter_AlsoWhenTheProviderHasARegistration()
    {
        var assembly = Compile(
            """
            public interface IStore { }

            [SingletonService(As = typeof(Store))]
            public class Store : IStore { }

            public class Worker([FromConstructor] Store built, Store resolved)
            {
                public Store Built => built;

                public Store Resolved => resolved;
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
        var registered = provider.GetRequiredService(assembly.Type("Store"));

        Verify(ReferenceEquals(registered, (object)worker.Resolved));
        Verify(!ReferenceEquals(registered, (object)worker.Built));
        Verify((object)worker.Built is not null);
    }

    [VerifyTest]
    public void FromServiceProvider_GetsTheParameterFromTheProvider_AndNotFromNewOrACaller()
    {
        var result = Run(
            """
            public class Helper { }

            public class Worker(
                [FromServiceProvider] Helper resolved,
                Helper built,
                [FromServiceProvider] string resolvedText,
                string callerText
            ) { }

            [SingletonService]
            public class Consumer(Func<string, Worker> createWorker) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id is "DM0030" or "DM0031"));

        Verify(
            Glue(result)
                .Contains(
                    "(arg0) => new global::TestNamespace.Worker("
                        + "provider.GetRequiredService<global::TestNamespace.Helper>(), "
                        + "(provider.GetService<global::TestNamespace.Helper>() ?? new global::TestNamespace.Helper()), "
                        + "provider.GetRequiredService<string>(), "
                        + "arg0)"
                )
        );
    }

    [VerifyTest]
    public void FromServiceProvider_OnAStringParameter_LetsTheGlueBuildTheClass()
    {
        var assembly = Compile(
            """
            public class Worker([FromServiceProvider] string name)
            {
                public string Name => name;
            }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker)
            {
                public string Name => createWorker().Name;
            }

            [DependencyModule]
            public partial class TestModule : IServiceCollectionConfiguration
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddSingleton("registered");
                }
            }
            """
        );

        using var provider = assembly.BuildProvider();
        dynamic consumer = provider.GetRequiredService(assembly.Type("Consumer"));

        Verify((string)consumer.Name == "registered");
    }

    [VerifyTest]
    [Row("IClock", "it is not a class")]
    [Row("string", "it is not a class")]
    [Row("AbstractHelper", "it is abstract")]
    [Row("PrivateHelper", "it has no constructor that the generated module can call")]
    public void FromConstructor_OnAParameterThatCannotBeBuilt_IsDM0031(
        string parameterType,
        string reason
    )
    {
        var result = Run(
            $$"""
            public abstract class AbstractHelper { }

            public class PrivateHelper
            {
                private PrivateHelper() { }
            }

            [IntoSet<Worker>]
            public class Worker([FromConstructor] {{parameterType}} helper) { }

            [SingletonService]
            public class Consumer(Func<Worker> createWorker) { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        // The set item and the Func build the same class. The parameter is reported one time.
        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0031");

        Verify(diagnostic.Severity == DiagnosticSeverity.Warning);
        Verify(
            diagnostic
                .GetMessage()
                .Contains("The parameter 'helper' of 'Worker' has [FromConstructor]")
        );
        Verify(diagnostic.GetMessage().Contains(reason));
        Verify(Glue(result).Contains("provider.GetRequiredService<"));
    }

    [VerifyTest]
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

        Verify((string)consumer.Name == "manual x");
        Single(
            assembly.Services,
            descriptor =>
                descriptor.ServiceType
                == typeof(Func<,>).MakeGenericType(typeof(string), assembly.Type("Worker"))
        );
    }

    [VerifyTest]
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

        Verify((int)forest.Tree.Depth == 4);
        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("IClock")),
                (object)forest.Tree.Clock
            )
        );
    }

    [VerifyTest]
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

        Verify((string)consumer.Name == "made");
    }

    [VerifyTest]
    [Row("Func<Task>")]
    [Row("Func<int>")]
    [Row("Func<string, bool>")]
    [Row("Func<string, IClock>")]
    [Row("Func<string, System.Text.StringBuilder>")]
    [Row("Func<IServiceProvider, IClock>")]
    [Row("Action<string>")]
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

        Verify(!result.GeneratedSources.Keys.Any(name => name.Contains("Glue")));
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0030"));
    }

    [VerifyTest]
    [Row(
        "Func<string, int, Worker>",
        "its constructor has no parameter of the type 'int' for the caller argument"
    )]
    [Row("Func<string, string, Worker>", "two caller arguments have the type 'string'")]
    [Row("Func<IClock, Worker>", "its constructor parameter 'name' has the type 'string'")]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0030");

        Verify(diagnostic.Severity == DiagnosticSeverity.Warning);
        Verify(diagnostic.GetMessage().Contains("'Consumer' takes a"));
        Verify(diagnostic.GetMessage().Contains(reason));
        Verify(!result.GeneratedSources.Keys.Any(name => name.Contains("Glue")));
    }

    [VerifyTest]
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

        var glueFile = Single(result.GeneratedSources.Keys, name => name.Contains("Glue"));

        Verify(glueFile.Contains("OtherModule"));
    }

    [VerifyTest]
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
        Verify(Glue(result).Contains("provider.GetRequiredService<global::TestNamespace.First>()"));
    }

    [VerifyTest]
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
        Verify(Glue(result) == Glue(Run(source)));

        Snapshot.Match(Glue(result));
    }
}

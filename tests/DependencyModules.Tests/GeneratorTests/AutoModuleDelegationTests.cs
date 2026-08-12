using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// A project with a <c>Program.cs</c> gets an <c>ApplicationModule</c> whether or not it declares a
/// module of its own. Both are modules with no realm restriction, so both register every service in
/// the compilation - and the generator used to emit that registration body twice, byte for byte.
/// In a 200 service project the duplicate was 5,413 bytes of IL, 44% of the assembly and 21% of the
/// ReadyToRun image, dead in every application that never names <c>ApplicationModule</c>.
///
/// It now defers instead: the auto module returns the declared one from <c>InternalGetModules</c>
/// and the runtime loads it. These tests pin both halves - that the duplicate is gone, and that
/// <c>AddModule&lt;ApplicationModule&gt;()</c> still registers exactly what it did before.
/// </summary>
public class AutoModuleDelegationTests {

    [Fact]
    public void ApplicationModule_DoesNotRepeatTheRegistrationsOfADeclaredModule() {
        var result = Run(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """));

        result.AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("TestModule.Dependencies"));
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("ApplicationModule.Dependencies"));
    }

    /// <summary>
    /// The class is still generated, and still reachable - only its registrations moved.
    /// </summary>
    [Fact]
    public void ApplicationModule_NamesTheModuleItDefersTo() {
        var result = Run(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """));

        result.AssertNoErrors();
        Assert.Contains("new global::TestNamespace.TestModule()", result.SourceContaining("ApplicationModule.Module"));
    }

    /// <summary>
    /// Decorations and interceptions travelled with the registrations, so they were duplicated the
    /// same way and have to stop being duplicated the same way.
    /// </summary>
    [Fact]
    public void ApplicationModule_DoesNotRepeatDecorationsEither() {
        var result = Run(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [Decorator]
            public class ThingDecorator(IThing inner) : IThing;

            [DependencyModule]
            public partial class TestModule;
            """));

        result.AssertNoErrors();

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("TestModule.Decorators"));
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("ApplicationModule.Decorators"));
    }

    /// <summary>
    /// With nothing to defer to, the auto module carries its own registrations exactly as before.
    /// </summary>
    [Fact]
    public void ApplicationModule_KeepsItsRegistrationsWhenNoModuleIsDeclared() {
        var result = Run(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;
            """));

        result.AssertNoErrors();
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("ApplicationModule.Dependencies"));
    }

    /// <summary>
    /// A realm-restricted module takes only the registrations aimed at it, so deferring to one would
    /// drop everything else. The auto module keeps its own registrations in that case.
    /// </summary>
    [Fact]
    public void ApplicationModule_KeepsItsRegistrationsWhenTheOnlyModuleIsRealmRestricted() {
        var result = Run(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule(OnlyRealm = true)]
            public partial class RealmModule;
            """));

        result.AssertNoErrors();
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("ApplicationModule.Dependencies"));
    }

    /// <summary>
    /// The point of the whole exercise: what reaches the service collection is unchanged.
    /// </summary>
    [Fact]
    public void ApplicationModule_RegistersTheSameServicesAsTheModuleItDefersTo() {
        var assembly = Compile(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """));

        var viaApplicationModule = Apply(assembly, "TestNamespace.ApplicationModule");
        var viaDeclaredModule = Apply(assembly, "TestNamespace.TestModule");

        var thing = assembly.GetType("TestNamespace.IThing")!;

        Assert.Equal(
            Describe(viaDeclaredModule, thing),
            Describe(viaApplicationModule, thing));

        Assert.NotNull(viaApplicationModule.BuildServiceProvider().GetService(thing));
    }

    /// <summary>
    /// Loading both used to apply every registration twice, because the two modules carried
    /// independent copies of it. The auto module now names the declared one, so the runtime's
    /// deduplication sees them as one.
    /// </summary>
    [Fact]
    public void LoadingBothModules_RegistersEachServiceOnce() {
        var assembly = Compile(TopLevelProgramWith(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """));

        var both = new ServiceCollection();
        both.AddModules(Module(assembly, "TestNamespace.ApplicationModule"), Module(assembly, "TestNamespace.TestModule"));

        var thing = assembly.GetType("TestNamespace.IThing")!;

        Assert.Single(both, descriptor => descriptor.ServiceType == thing);
    }

    private static string Describe(IServiceCollection services, Type serviceType) =>
        string.Join(
            ", ",
            services
                .Where(descriptor => descriptor.ServiceType == serviceType)
                .Select(descriptor => $"{descriptor.Lifetime}:{descriptor.ImplementationType?.FullName}"));

    private static IServiceCollection Apply(Assembly assembly, string moduleName) {
        var services = new ServiceCollection();

        services.AddModules(Module(assembly, moduleName));

        return services;
    }

    private static IDependencyModule Module(Assembly assembly, string moduleName) {
        var type = assembly.GetType(moduleName)
                   ?? throw new InvalidOperationException(
                       $"No type '{moduleName}'. Present: " +
                       string.Join(", ", assembly.GetTypes().Select(t => t.FullName)));

        return (IDependencyModule)Activator.CreateInstance(type)!;
    }

    private static Assembly Compile(IReadOnlyDictionary<string, string> sources) {
        var result = GeneratorTestHarness.Run(
            sources,
            null,
            OutputKind.ConsoleApplication,
            assemblyName: "AutoModuleDelegation" + Interlocked.Increment(ref _counter));

        result.AssertNoErrors();

        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);

        Assert.True(
            emitted.Success,
            string.Join(
                Environment.NewLine,
                emitted.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => $"  {diagnostic.Id} {diagnostic.GetMessage()}")));

        return Assembly.Load(stream.ToArray());
    }

    private static int _counter;

    private static GeneratorResult Run(IReadOnlyDictionary<string, string> sources) =>
        GeneratorTestHarness.Run(sources, null, OutputKind.ConsoleApplication);

    private static Dictionary<string, string> TopLevelProgramWith(string services) =>
        new() {
            ["Program.cs"] =
                """
                System.Console.WriteLine("hello");
                """,
            ["Services.cs"] =
                $$"""
                  using DependencyModules.Runtime.Attributes;

                  namespace TestNamespace;

                  {{services}}
                  """
        };
}

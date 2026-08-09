using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Snapshots of the generator's full output. These exist to make any change to generated code
/// visible in review — the generated file is the library's real public surface, and a change to it
/// lands in every consumer's build.
///
/// To accept intentional changes:
///     UPDATE_SNAPSHOTS=1 dotnet test tests/DependencyModules.Tests
/// then review the diff under tests/DependencyModules.Tests/Snapshots.
/// </summary>
public class ModuleGenerationSnapshotTests {

    [Fact]
    public void SimpleModule() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    /// <summary>
    /// The shape of an environment-conditional registration: the guard, the environment parameter
    /// that appears only because something in the module needs it, and the unconditional service
    /// staying outside the guard.
    ///
    /// Also the ordering rule. FakeEmailSender sorts before SmtpEmailSender by name, but has to be
    /// emitted after it, because the container resolves a single service from the last matching
    /// descriptor and the conditional registration is the override.
    /// </summary>
    [Fact]
    public void ModuleWithEnvironmentConditions() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IAlways;
            public interface IEmailSender;
            public interface IBilling;

            [SingletonService]
            public class Always : IAlways;

            [SingletonService]
            public class SmtpEmailSender : IEmailSender;

            [SingletonService]
            [IfEnvironment("Development", "Staging")]
            public class FakeEmailSender : IEmailSender;

            [SingletonService]
            [IfNotEnvironment("Production")]
            [IfEnvironmentValue("FEATURE_BILLING", "on")]
            public class Billing : IBilling;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void ModuleWithAllServiceLifetimes() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface ISingleton;
            public interface IScoped;
            public interface ITransient;

            [SingletonService]
            public class SingletonThing : ISingleton;

            [ScopedService]
            public class ScopedThing : IScoped;

            [TransientService]
            public class TransientThing : ITransient;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void ModuleWithConstructorParametersAndProperties() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interfaces;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule(bool someFlag) : IServiceCollectionConfiguration {
                public string OptionalString { get; set; } = "";

                public void ConfigureServices(IServiceCollection services) {
                }
            }
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void KeyedAndAsRegistrations() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;
            public interface IOther;

            [SingletonService(Key = "the-key")]
            public class KeyedThing : IThing;

            [SingletonService(As = typeof(IOther))]
            public class AsThing : IThing, IOther;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void RegistrationTypeVariants() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface ITry;
            public interface ITryEnumerable;
            public interface IReplace;

            [SingletonService(Using = RegistrationType.Try)]
            public class TryThing : ITry;

            [SingletonService(Using = RegistrationType.TryEnumerable)]
            public class TryEnumerableThing : ITryEnumerable;

            [SingletonService(Using = RegistrationType.Replace)]
            public class ReplaceThing : IReplace;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void RecordModule() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial record TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void GenericServiceRegistrations() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGeneric<T>;

            [SingletonService]
            public class OpenGeneric<T> : IGeneric<T>;

            [SingletonService]
            public class ClosedGeneric : IGeneric<string>;

            [DependencyModule]
            public partial class TestModule;
            """);

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }

    [Fact]
    public void ModuleWithCoverageExclusionDisabled() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing;

            [DependencyModule]
            public partial class TestModule;
            """,
            new Dictionary<string, string> { ["ExcludeGeneratedCodeFromCoverage"] = "false" });

        result.AssertNoErrors();
        Snapshot.Match(result.ToSnapshot());
    }
}

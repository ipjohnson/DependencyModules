using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Behavioural tests for environment-conditional registration.
/// </summary>
/// <remarks>
/// Each case compiles, loads and applies the module under a given environment, then asserts what is
/// actually in the collection. A condition emitted into the wrong branch still produces plausible
/// generated text, so text assertions would pass while the service registered in production.
/// </remarks>
public class EnvironmentConditionTests {

    private const string Preamble =
        """
        using System;
        using DependencyModules.Runtime.Attributes;

        namespace TestNamespace;

        """;

    private static GeneratedAssembly Compile(string source, IModuleEnvironment? environment) =>
        GeneratedAssembly.Create(Preamble + source, environment: environment);

    private static IModuleEnvironment Env(string name) => new ModuleEnvironment(name);

    private static IModuleEnvironment Env(string name, params (string Key, string? Value)[] values) =>
        new ModuleEnvironment(name, values.ToDictionary(v => v.Key, v => v.Value));

    private const string NameGated =
        """
        public interface IEmailSender { }

        [SingletonService]
        [IfEnvironment("Development", "Staging")]
        public class FakeEmailSender : IEmailSender { }
        """;

    private const string Module =
        """

        [DependencyModule]
        public partial class TestModule;
        """;

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    [InlineData("development", true)]
    [InlineData("Production", false)]
    [InlineData("", false)]
    public void IfEnvironmentRegistersOnlyInTheNamedEnvironments(string environmentName, bool expected) {
        var assembly = Compile(NameGated + Module, Env(environmentName));

        Assert.Equal(expected, assembly.Descriptors("IEmailSender").Count == 1);
    }

    [Fact]
    public void IfNotEnvironmentRegistersEverywhereElse() {
        const string source =
            """
            public interface IProfiler { }

            [SingletonService]
            [IfNotEnvironment("Production")]
            public class RequestProfiler : IProfiler { }
            """;

        Assert.Single(Compile(source + Module, Env("Development")).Descriptors("IProfiler"));
        Assert.Empty(Compile(source + Module, Env("Production")).Descriptors("IProfiler"));
    }

    [Fact]
    public void IfEnvironmentValueTestsPresenceWhenGivenOnlyAKey() {
        const string source =
            """
            public interface IBilling { }

            [SingletonService]
            [IfEnvironmentValue("FEATURE_BILLING")]
            public class Billing : IBilling { }
            """;

        Assert.Single(Compile(source + Module, Env("Any", ("FEATURE_BILLING", "anything"))).Descriptors("IBilling"));

        // Set to empty is still set; only absence is absence.
        Assert.Single(Compile(source + Module, Env("Any", ("FEATURE_BILLING", ""))).Descriptors("IBilling"));

        Assert.Empty(Compile(source + Module, Env("Any")).Descriptors("IBilling"));
    }

    [Fact]
    public void IfEnvironmentValueComparesTheValueExactly() {
        const string source =
            """
            public interface IBilling { }

            [SingletonService]
            [IfEnvironmentValue("MODE", "on")]
            public class Billing : IBilling { }
            """;

        Assert.Single(Compile(source + Module, Env("Any", ("MODE", "on"))).Descriptors("IBilling"));

        // Values are data, so unlike environment names they are compared ordinally.
        Assert.Empty(Compile(source + Module, Env("Any", ("MODE", "On"))).Descriptors("IBilling"));
        Assert.Empty(Compile(source + Module, Env("Any", ("MODE", "off"))).Descriptors("IBilling"));
    }

    [Fact]
    public void ConditionsOfDifferentKindsCombineWithAnd() {
        const string source =
            """
            public interface IThing { }

            [SingletonService]
            [IfEnvironment("Development")]
            [IfEnvironmentValue("MODE", "on")]
            public class Thing : IThing { }
            """;

        Assert.Single(Compile(source + Module, Env("Development", ("MODE", "on"))).Descriptors("IThing"));
        Assert.Empty(Compile(source + Module, Env("Development", ("MODE", "off"))).Descriptors("IThing"));
        Assert.Empty(Compile(source + Module, Env("Production", ("MODE", "on"))).Descriptors("IThing"));
    }

    [Fact]
    public void SeveralValueConditionsAllHaveToHold() {
        const string source =
            """
            public interface IThing { }

            [SingletonService]
            [IfEnvironmentValue("A", "1")]
            [IfEnvironmentValue("B", "2")]
            public class Thing : IThing { }
            """;

        Assert.Single(Compile(source + Module, Env("Any", ("A", "1"), ("B", "2"))).Descriptors("IThing"));
        Assert.Empty(Compile(source + Module, Env("Any", ("A", "1"))).Descriptors("IThing"));
    }

    /// <summary>
    /// A conditional registration has to be able to override an unconditional default for the same
    /// service type.
    /// </summary>
    /// <remarks>
    /// The container resolves a single service from the last matching descriptor, so this only works
    /// if conditional registrations are emitted after unconditional ones. Both orderings compile and
    /// both look right in the generated file; only one of them registers the override.
    ///
    /// The names are deliberate. Service models are emitted in name order, so "Fake" sorts before
    /// "Smtp" and the default would land last and win in every environment.
    /// </remarks>
    [Fact]
    public void AConditionalRegistrationOverridesAnUnconditionalDefault() {
        const string source =
            """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var development = Compile(source + Module, Env("Development"));
        var production = Compile(source + Module, Env("Production"));

        Assert.Equal(
            development.Type("FakeEmailSender"),
            development.BuildProvider().GetService(development.Type("IEmailSender"))!.GetType());

        Assert.Equal(
            production.Type("SmtpEmailSender"),
            production.BuildProvider().GetService(production.Type("IEmailSender"))!.GetType());
    }

    /// <summary>
    /// The motivating case: one service type, two implementations, exactly one registered.
    /// </summary>
    [Fact]
    public void TwoImplementationsOfOneServiceSelectByEnvironment() {
        const string source =
            """
            public interface IEmailSender { string Name { get; } }

            [SingletonService]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { public string Name => "fake"; }

            [SingletonService]
            [IfNotEnvironment("Development")]
            public class SmtpEmailSender : IEmailSender { public string Name => "smtp"; }
            """;

        var development = Compile(source + Module, Env("Development"));
        var production = Compile(source + Module, Env("Production"));

        Assert.Equal(development.Type("FakeEmailSender"), development.Descriptor("IEmailSender").ImplementationType);
        Assert.Equal(production.Type("SmtpEmailSender"), production.Descriptor("IEmailSender").ImplementationType);
    }

    [Fact]
    public void UnconditionalServicesInTheSameModuleAreUnaffected() {
        const string source =
            """
            public interface IAlways { }
            public interface ISometimes { }

            [SingletonService]
            public class Always : IAlways { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class Sometimes : ISometimes { }
            """;

        var production = Compile(source + Module, Env("Production"));

        Assert.Single(production.Descriptors("IAlways"));
        Assert.Empty(production.Descriptors("ISometimes"));
    }

    [Fact]
    public void ModuleEnvironmentNoneRegistersNothingConditional() {
        var assembly = Compile(NameGated + Module, ModuleEnvironment.None);

        Assert.Empty(assembly.Descriptors("IEmailSender"));
    }

    /// <summary>
    /// No environment supplied behaves exactly as if the process environment had been passed.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="ModuleEnvironment.Default"/> rather than against "Production", so
    /// that this does not depend on what the variables happen to be while the suite runs — which
    /// ModuleEnvironmentDefaultNameTests moves on purpose.
    /// </remarks>
    [Fact]
    public void NoEnvironmentSuppliedUsesTheProcessEnvironment() {
        var supplied = Compile(NameGated + Module, ModuleEnvironment.Default);
        var omitted = Compile(NameGated + Module, environment: null);

        Assert.Equal(
            supplied.Descriptors("IEmailSender").Count,
            omitted.Descriptors("IEmailSender").Count);
    }

    /// <summary>
    /// Across modules, module order decides — a referenced module's conditional registration does
    /// not jump ahead of the registrations of the module that references it.
    /// </summary>
    /// <remarks>
    /// This is what makes a global "apply all conditionals last" phase, like the one decorators get,
    /// the wrong shape. Modules are expanded dependencies-first, so the referencing module applies
    /// last and its registrations win. A conditional phase would let a library's
    /// <c>[IfEnvironment]</c> beat an application's own implementation, inverting a precedence the
    /// application controls and can see.
    ///
    /// A condition says "instead of my other registration, here". It does not say "instead of
    /// yours".
    /// </remarks>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AReferencedModuleConditionalDoesNotOverrideTheReferencingModule(string environmentName) {
        const string source =
            """
            public interface IEmailSender { }

            [SingletonService(Realm = typeof(LibraryModule))]
            [IfEnvironment("Development")]
            public class LibraryFake : IEmailSender { }

            [SingletonService(Realm = typeof(TestModule))]
            public class ApplicationOwn : IEmailSender { }

            [DependencyModule]
            public partial class LibraryModule;

            [LibraryModule]
            [DependencyModule]
            public partial class TestModule;
            """;

        var assembly = Compile(source, Env(environmentName));

        Assert.Equal(
            assembly.Type("ApplicationOwn"),
            assembly.BuildProvider().GetService(assembly.Type("IEmailSender"))!.GetType());
    }

    /// <summary>
    /// The direction that has to work: a referenced module registers the default, and the
    /// application conditionally overrides it.
    /// </summary>
    [Theory]
    [InlineData("Development", "ApplicationFake")]
    [InlineData("Production", "LibrarySmtp")]
    public void AnApplicationConditionalOverridesAReferencedModuleDefault(
        string environmentName, string expected) {

        const string source =
            """
            public interface IEmailSender { }

            [SingletonService(Realm = typeof(LibraryModule))]
            public class LibrarySmtp : IEmailSender { }

            [SingletonService(Realm = typeof(TestModule))]
            [IfEnvironment("Development")]
            public class ApplicationFake : IEmailSender { }

            [DependencyModule]
            public partial class LibraryModule;

            [LibraryModule]
            [DependencyModule]
            public partial class TestModule;
            """;

        var assembly = Compile(source, Env(environmentName));

        Assert.Equal(
            assembly.Type(expected),
            assembly.BuildProvider().GetService(assembly.Type("IEmailSender"))!.GetType());
    }

    /// <summary>
    /// A conditional registration is additive: it shadows the default for a single resolve, and
    /// both are still in the enumerable.
    /// </summary>
    [Fact]
    public void AConditionalOverrideLeavesTheDefaultInTheEnumerable() {
        const string source =
            """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var assembly = Compile(source + Module, Env("Development"));

        Assert.Equal(2, assembly.Descriptors("IEmailSender").Count);
    }

    /// <summary>
    /// "When" and "how" are separate. A condition says when a registration happens; Using says what
    /// kind of registration it is, and the two compose.
    /// </summary>
    [Fact]
    public void AConditionCombinesWithReplace() {
        const string source =
            """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService(Using = RegistrationType.Replace)]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var development = Compile(source + Module, Env("Development"));
        var production = Compile(source + Module, Env("Production"));

        Assert.Single(development.Descriptors("IEmailSender"));
        Assert.Equal(development.Type("FakeEmailSender"), development.Descriptor("IEmailSender").ImplementationType);

        Assert.Single(production.Descriptors("IEmailSender"));
        Assert.Equal(production.Type("SmtpEmailSender"), production.Descriptor("IEmailSender").ImplementationType);
    }

    [Fact]
    public void ConditionsAreReportedAtBuildTime() {
        var result = GeneratorTestHarness.Run(Preamble + NameGated + Module);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0011");

        Assert.Contains("Development or Staging", diagnostic.GetMessage());
    }

    [Fact]
    public void UnconditionalServicesReportNothing() {
        const string source =
            """
            public interface IThing { }

            [SingletonService]
            public class Thing : IThing { }
            """;

        var result = GeneratorTestHarness.Run(Preamble + source + Module);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id is "DM0011" or "DM0012");
    }

    [Theory]
    [InlineData("[IfEnvironment]", "environment name")]
    [InlineData("[IfNotEnvironment]", "environment name")]
    [InlineData("[IfEnvironmentValue(\"\")]", "key")]
    public void AConditionThatTestsNothingIsRefused(string attribute, string expectedKind) {
        var source =
            $$"""
              public interface IThing { }

              [SingletonService]
              {{attribute}}
              public class Thing : IThing { }
              """;

        var result = GeneratorTestHarness.Run(Preamble + source + Module);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");

        Assert.Contains(expectedKind, diagnostic.GetMessage());

        // Reported, not silently dropped: the service still registers rather than vanishing.
        Assert.Single(
            GeneratedAssembly.Create(Preamble + source + Module, environment: ModuleEnvironment.None)
                .Descriptors("IThing"));
    }

    /// <summary>
    /// A const rather than a literal has to read as the string it evaluates to, or a codebase that
    /// keeps its environment names in one place would silently never match.
    /// </summary>
    [Fact]
    public void ConditionArgumentsMayBeConstants() {
        const string source =
            """
            public static class Environments {
                public const string Development = "Development";
            }

            public interface IThing { }

            [SingletonService]
            [IfEnvironment(Environments.Development)]
            public class Thing : IThing { }
            """;

        Assert.Single(Compile(source + Module, Env("Development")).Descriptors("IThing"));
        Assert.Empty(Compile(source + Module, Env("Production")).Descriptors("IThing"));
    }

    /// <summary>
    /// The attribute resolved through the semantic model rather than matched on how it was written.
    /// A namespace-qualified usage silently matching nothing is a bug this generator has had once
    /// already, and here it would register a development service in production.
    /// </summary>
    [Fact]
    public void ANamespaceQualifiedConditionStillApplies() {
        const string source =
            """
            public interface IThing { }

            [SingletonService]
            [DependencyModules.Runtime.Attributes.IfEnvironment("Development")]
            public class Thing : IThing { }
            """;

        Assert.Single(Compile(source + Module, Env("Development")).Descriptors("IThing"));
        Assert.Empty(Compile(source + Module, Env("Production")).Descriptors("IThing"));
    }
}

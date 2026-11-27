using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Behavioural tests for environment-conditional registration.
/// </summary>
/// <remarks>
/// Each case compiles, loads and applies the module under a given environment, then asserts what is
/// actually in the collection. A condition emitted into the wrong branch still produces plausible
/// generated text, so text assertions would pass while the service registered in production.
/// </remarks>
public partial class EnvironmentConditionTests
{
    private const string Preamble = """
        using System;
        using DependencyModules.Runtime.Attributes;

        namespace TestNamespace;

        """;

    private static GeneratedAssembly Compile(string source, IModuleEnvironment? environment) =>
        GeneratedAssembly.Create(Preamble + source, environment: environment);

    /// <summary>
    /// Pinned to the values written here. These assert which registrations a given set of values
    /// produces, so a variable set on the machine running them must not reach a key they never name.
    /// </summary>
    private static IModuleEnvironment Env(string name) => Env(name, []);

    /// <inheritdoc cref="Env(string)" />
    private static IModuleEnvironment Env(
        string name,
        params (string Key, string? Value)[] values
    ) => new ModuleEnvironment(false, name, values.ToDictionary(v => v.Key, v => v.Value));

    private const string NameGated = """
        public interface IEmailSender { }

        [SingletonService]
        [IfEnvironment("Development", "Staging")]
        public class FakeEmailSender : IEmailSender { }
        """;

    private const string Module = """

        [DependencyModule]
        public partial class TestModule;
        """;

    [VerifyTest]
    [Row("Development", true)]
    [Row("Staging", true)]
    [Row("development", true)]
    [Row("Production", false)]
    [Row("", false)]
    public void IfEnvironmentRegistersOnlyInTheNamedEnvironments(
        string environmentName,
        bool expected
    )
    {
        var assembly = Compile(NameGated + Module, Env(environmentName));

        Verify((assembly.Descriptors("IEmailSender").Count == 1) == expected);
    }

    [VerifyTest]
    public void IfNotEnvironmentRegistersEverywhereElse()
    {
        const string source = """
            public interface IProfiler { }

            [SingletonService]
            [IfNotEnvironment("Production")]
            public class RequestProfiler : IProfiler { }
            """;

        Single(Compile(source + Module, Env("Development")).Descriptors("IProfiler"));
        Verify(Compile(source + Module, Env("Production")).Descriptors("IProfiler").Count == 0);
    }

    [VerifyTest]
    public void IfEnvironmentValueTestsPresenceWhenGivenOnlyAKey()
    {
        const string source = """
            public interface IBilling { }

            [SingletonService]
            [IfEnvironmentValue("FEATURE_BILLING")]
            public class Billing : IBilling { }
            """;

        Single(
            Compile(source + Module, Env("Any", ("FEATURE_BILLING", "anything")))
                .Descriptors("IBilling")
        );

        // Set to empty is still set; only absence is absence.
        Single(
            Compile(source + Module, Env("Any", ("FEATURE_BILLING", ""))).Descriptors("IBilling")
        );

        Verify(Compile(source + Module, Env("Any")).Descriptors("IBilling").Count == 0);
    }

    [VerifyTest]
    public void IfEnvironmentValueComparesTheValueExactly()
    {
        const string source = """
            public interface IBilling { }

            [SingletonService]
            [IfEnvironmentValue("MODE", "on")]
            public class Billing : IBilling { }
            """;

        Single(Compile(source + Module, Env("Any", ("MODE", "on"))).Descriptors("IBilling"));

        // Values are data, so unlike environment names they are compared ordinally.
        Verify(
            Compile(source + Module, Env("Any", ("MODE", "On"))).Descriptors("IBilling").Count == 0
        );
        Verify(
            Compile(source + Module, Env("Any", ("MODE", "off"))).Descriptors("IBilling").Count == 0
        );
    }

    [VerifyTest]
    public void ConditionsOfDifferentKindsCombineWithAnd()
    {
        const string source = """
            public interface IThing { }

            [SingletonService]
            [IfEnvironment("Development")]
            [IfEnvironmentValue("MODE", "on")]
            public class Thing : IThing { }
            """;

        Single(Compile(source + Module, Env("Development", ("MODE", "on"))).Descriptors("IThing"));
        Verify(
            Compile(source + Module, Env("Development", ("MODE", "off")))
                .Descriptors("IThing")
                .Count == 0
        );
        Verify(
            Compile(source + Module, Env("Production", ("MODE", "on"))).Descriptors("IThing").Count
                == 0
        );
    }

    [VerifyTest]
    public void SeveralValueConditionsAllHaveToHold()
    {
        const string source = """
            public interface IThing { }

            [SingletonService]
            [IfEnvironmentValue("A", "1")]
            [IfEnvironmentValue("B", "2")]
            public class Thing : IThing { }
            """;

        Single(Compile(source + Module, Env("Any", ("A", "1"), ("B", "2"))).Descriptors("IThing"));
        Verify(Compile(source + Module, Env("Any", ("A", "1"))).Descriptors("IThing").Count == 0);
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
    [VerifyTest]
    public void AConditionalRegistrationOverridesAnUnconditionalDefault()
    {
        const string source = """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var development = Compile(source + Module, Env("Development"));
        var production = Compile(source + Module, Env("Production"));

        Verify(
            development.BuildProvider().GetService(development.Type("IEmailSender"))!.GetType()
                == development.Type("FakeEmailSender")
        );

        Verify(
            production.BuildProvider().GetService(production.Type("IEmailSender"))!.GetType()
                == production.Type("SmtpEmailSender")
        );
    }

    /// <summary>
    /// The motivating case: one service type, two implementations, exactly one registered.
    /// </summary>
    [VerifyTest]
    public void TwoImplementationsOfOneServiceSelectByEnvironment()
    {
        const string source = """
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

        Verify(
            development.Descriptor("IEmailSender").ImplementationType
                == development.Type("FakeEmailSender")
        );
        Verify(
            production.Descriptor("IEmailSender").ImplementationType
                == production.Type("SmtpEmailSender")
        );
    }

    [VerifyTest]
    public void UnconditionalServicesInTheSameModuleAreUnaffected()
    {
        const string source = """
            public interface IAlways { }
            public interface ISometimes { }

            [SingletonService]
            public class Always : IAlways { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class Sometimes : ISometimes { }
            """;

        var production = Compile(source + Module, Env("Production"));

        Single(production.Descriptors("IAlways"));
        Verify(production.Descriptors("ISometimes").Count == 0);
    }

    [VerifyTest]
    public void ModuleEnvironmentNoneRegistersNothingConditional()
    {
        var assembly = Compile(NameGated + Module, ModuleEnvironment.None);

        Verify(assembly.Descriptors("IEmailSender").Count == 0);
    }

    /// <summary>
    /// No environment supplied behaves exactly as if the process environment had been passed.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="ModuleEnvironment.Default"/> rather than against "Production", so
    /// that this does not depend on what the variables happen to be while the suite runs — which
    /// ModuleEnvironmentDefaultNameTests moves on purpose.
    /// </remarks>
    [VerifyTest]
    public void NoEnvironmentSuppliedUsesTheProcessEnvironment()
    {
        var supplied = Compile(NameGated + Module, ModuleEnvironment.CreateDefault());
        var omitted = Compile(NameGated + Module, environment: null);

        Verify(
            omitted.Descriptors("IEmailSender").Count == supplied.Descriptors("IEmailSender").Count
        );
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
    [VerifyTest]
    [Row("Development")]
    [Row("Production")]
    public void AReferencedModuleConditionalDoesNotOverrideTheReferencingModule(
        string environmentName
    )
    {
        const string source = """
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

        Verify(
            assembly.BuildProvider().GetService(assembly.Type("IEmailSender"))!.GetType()
                == assembly.Type("ApplicationOwn")
        );
    }

    /// <summary>
    /// The direction that has to work: a referenced module registers the default, and the
    /// application conditionally overrides it.
    /// </summary>
    [VerifyTest]
    [Row("Development", "ApplicationFake")]
    [Row("Production", "LibrarySmtp")]
    public void AnApplicationConditionalOverridesAReferencedModuleDefault(
        string environmentName,
        string expected
    )
    {
        const string source = """
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

        Verify(
            assembly.BuildProvider().GetService(assembly.Type("IEmailSender"))!.GetType()
                == assembly.Type(expected)
        );
    }

    /// <summary>
    /// A conditional registration is additive: it shadows the default for a single resolve, and
    /// both are still in the enumerable.
    /// </summary>
    [VerifyTest]
    public void AConditionalOverrideLeavesTheDefaultInTheEnumerable()
    {
        const string source = """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var assembly = Compile(source + Module, Env("Development"));

        Verify(assembly.Descriptors("IEmailSender").Count == 2);
    }

    /// <summary>
    /// "When" and "how" are separate. A condition says when a registration happens; Using says what
    /// kind of registration it is, and the two compose.
    /// </summary>
    [VerifyTest]
    public void AConditionCombinesWithReplace()
    {
        const string source = """
            public interface IEmailSender { }

            [SingletonService]
            public class SmtpEmailSender : IEmailSender { }

            [SingletonService(Using = RegistrationType.Replace)]
            [IfEnvironment("Development")]
            public class FakeEmailSender : IEmailSender { }
            """;

        var development = Compile(source + Module, Env("Development"));
        var production = Compile(source + Module, Env("Production"));

        Single(development.Descriptors("IEmailSender"));
        Verify(
            development.Descriptor("IEmailSender").ImplementationType
                == development.Type("FakeEmailSender")
        );

        Single(production.Descriptors("IEmailSender"));
        Verify(
            production.Descriptor("IEmailSender").ImplementationType
                == production.Type("SmtpEmailSender")
        );
    }

    [VerifyTest]
    public void ConditionsAreReportedAtBuildTime()
    {
        var result = GeneratorTestHarness.Run(Preamble + NameGated + Module);

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0011");

        Verify(diagnostic.GetMessage().Contains("Development or Staging"));
    }

    [VerifyTest]
    public void UnconditionalServicesReportNothing()
    {
        const string source = """
            public interface IThing { }

            [SingletonService]
            public class Thing : IThing { }
            """;

        var result = GeneratorTestHarness.Run(Preamble + source + Module);

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id is "DM0011" or "DM0012"));
    }

    [VerifyTest]
    [Row("[IfEnvironment]", "environment name")]
    [Row("[IfNotEnvironment]", "environment name")]
    [Row("[IfEnvironmentValue(\"\")]", "key")]
    public void AConditionThatTestsNothingIsRefused(string attribute, string expectedKind)
    {
        var source = $$"""
            public interface IThing { }

            [SingletonService]
            {{attribute}}
            public class Thing : IThing { }
            """;

        var result = GeneratorTestHarness.Run(Preamble + source + Module);

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");

        Verify(diagnostic.GetMessage().Contains(expectedKind));

        // Reported, not silently dropped: the service still registers rather than vanishing.
        Single(
            GeneratedAssembly
                .Create(Preamble + source + Module, environment: ModuleEnvironment.None)
                .Descriptors("IThing")
        );
    }

    [VerifyTest]
    public void AConditionThatTestsNothingOnAConventionClassIsRefused()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IRule { }

            [IfEnvironment]
            public class EmptyRule : IRule { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IRule>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");

        Verify(diagnostic.GetMessage().Contains("EmptyRule"));
    }

    [VerifyTest]
    public void AConditionThatTestsNothingOnADecoratorIsRefused()
    {
        var result = GeneratorTestHarness.Run(
            Preamble
                + """
                public interface IThing { }

                [SingletonService]
                public class Thing : IThing { }

                [Decorator]
                [IfEnvironmentValue("")]
                public class LoggingThing(IThing inner) : IThing { }
                """
                + Module
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0012");

        Verify(diagnostic.GetMessage().Contains("LoggingThing"));
    }

    /// <summary>
    /// A const rather than a literal has to read as the string it evaluates to, or a codebase that
    /// keeps its environment names in one place would silently never match.
    /// </summary>
    [VerifyTest]
    public void ConditionArgumentsMayBeConstants()
    {
        const string source = """
            public static class Environments {
                public const string Development = "Development";
            }

            public interface IThing { }

            [SingletonService]
            [IfEnvironment(Environments.Development)]
            public class Thing : IThing { }
            """;

        Single(Compile(source + Module, Env("Development")).Descriptors("IThing"));
        Verify(Compile(source + Module, Env("Production")).Descriptors("IThing").Count == 0);
    }

    /// <summary>
    /// The attribute resolved through the semantic model rather than matched on how it was written.
    /// A namespace-qualified usage silently matching nothing is a bug this generator has had once
    /// already, and here it would register a development service in production.
    /// </summary>
    [VerifyTest]
    public void ANamespaceQualifiedConditionStillApplies()
    {
        const string source = """
            public interface IThing { }

            [SingletonService]
            [DependencyModules.Runtime.Attributes.IfEnvironment("Development")]
            public class Thing : IThing { }
            """;

        Single(Compile(source + Module, Env("Development")).Descriptors("IThing"));
        Verify(Compile(source + Module, Env("Production")).Descriptors("IThing").Count == 0);
    }
}

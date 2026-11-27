using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Where an interception's applicator is emitted, when realms are in play.
///
/// Three filters decide placement — one for registrations, one for decorations, one for
/// interceptions — and all three read the same rule: named a realm, it belongs to that module;
/// named none, it belongs to every module that is not <c>OnlyRealm</c>. They agree whenever the
/// registration and the interception are scoped the same way, and they diverge when the
/// registration names a realm and the interception does not: the registration lands only in the
/// named module, the applicator lands everywhere except it, and the interception is dead in every
/// container that could exist. Nothing was reported, because each half was individually correct.
///
/// The field report filed this twice without noticing it was once. Agent 05 reached it deliberately
/// with a realm-scoped service; agent 08 reached it by accident through a convention, because
/// convention registrations are always stamped with their declaring module's realm.
/// </summary>
public partial class InterceptionRealmTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;
        using DependencyModules.Runtime.Interception;

        namespace TestNamespace;

        public interface IGreeter { string Greet(); }

        public sealed class CountingInterceptor : IInterceptor {
            public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
        }

        """;

    /// <summary>
    /// The service names a realm; the interception does not. The applicator has to follow the
    /// registration, because a per-implementation interception is about that one registration.
    /// </summary>
    [VerifyTest]
    public void ARealmScopedService_WithAnUnrealmedInterception_IsInterceptedInThatRealm()
    {
        var result = Run(
                """
                [DependencyModule(OnlyRealm = true)]
                public partial class RealmModule;

                [SingletonService(Realm = typeof(RealmModule))]
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class TestModule;
                """
            )
            .AssertNoErrors();

        Verify(result.SourceContaining("RealmModule.Interceptors").Contains("Greeter_Intercepted"));
    }

    /// <summary>
    /// And nowhere else. A module that does not register the implementation has nothing to apply the
    /// wrapper to, so an applicator there is dead weight at best.
    /// </summary>
    [VerifyTest]
    public void ARealmScopedService_IsNotInterceptedOutsideItsRealm()
    {
        var result = Run(
                """
                [DependencyModule(OnlyRealm = true)]
                public partial class RealmModule;

                [SingletonService(Realm = typeof(RealmModule))]
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class TestModule;
                """
            )
            .AssertNoErrors();

        Verify(!result.SourceContaining("TestModule.Interceptors").Contains("Greeter_Intercepted"));
    }

    /// <summary>
    /// An explicit <c>[Intercept(Realm = ...)]</c> still wins outright. It is the escape hatch the
    /// changelog documents, and following the registration must not take it away.
    /// </summary>
    [VerifyTest]
    public void AnExplicitInterceptRealm_StillDecides()
    {
        var result = Run(
                """
                [DependencyModule(OnlyRealm = true)]
                public partial class RealmModule;

                [SingletonService]
                [Intercept(typeof(CountingInterceptor), Realm = typeof(RealmModule))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class TestModule;
                """
            )
            .AssertNoErrors();

        Verify(result.SourceContaining("RealmModule.Interceptors").Contains("Greeter_Intercepted"));
        Verify(!result.SourceContaining("TestModule.Interceptors").Contains("Greeter_Intercepted"));
    }

    /// <summary>
    /// The ordinary case, unchanged: nothing names a realm, so the interception belongs to every
    /// module that is not realm-only.
    /// </summary>
    [VerifyTest]
    public void AnUnrealmedService_WithAnUnrealmedInterception_IsInterceptedNormally()
    {
        var result = Run(
                """
                [SingletonService]
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class TestModule;
                """
            )
            .AssertNoErrors();

        Verify(result.SourceContaining("TestModule.Interceptors").Contains("Greeter_Intercepted"));
    }

    /// <summary>
    /// The convention route into the same divergence, and the half that has no fix at this layer.
    ///
    /// A convention registration is stamped with its declaring module's realm at match time -
    /// deliberately, so an OnlyRealm module does not drop its own convention registrations - which
    /// is long after the interception model was built and in a different generator. So the
    /// interception cannot inherit that realm the way it now inherits a service attribute's. Left
    /// alone it was silent; it is now DM0020, which says the interceptors never run and names the
    /// property that fixes it.
    ///
    /// Widening the placement rule instead was the alternative and is worse: an OnlyRealm module
    /// would receive applicators for interceptions on classes it does not register, and each
    /// applicator registers its interceptor - which is exactly the leak 1.1.0 closed, where an
    /// interceptor needing a dependency the isolated container lacks threw while building the
    /// provider.
    /// </summary>
    [VerifyTest]
    public void AConventionRegisteredClass_InAnOnlyRealmModule_ReportsDM0020()
    {
        var result = Run(
            """
            [Intercept(typeof(CountingInterceptor))]
            public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

            [DependencyModule(OnlyRealm = true)]
            public partial class ConventionModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0020");

        Verify(diagnostic.GetMessage().Contains("Greeter"));
        Verify(diagnostic.GetMessage().Contains("Realm"));
    }

    /// <summary>
    /// An interception a module does apply is not reported. The diagnostic is about a wrapper that
    /// can never run, not about every realm arrangement that looks unusual.
    /// </summary>
    [VerifyTest]
    public void AnInterceptionSomeModuleApplies_IsNotReported()
    {
        var result = Run(
            """
            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0020"));
    }

    /// <summary>
    /// Nor is the realm-scoped case that now works: the interception follows the registration, so a
    /// module applies it and there is nothing to report.
    /// </summary>
    [VerifyTest]
    public void ARealmScopedServiceFollowingItsRegistration_IsNotReported()
    {
        var result = Run(
            """
            [DependencyModule(OnlyRealm = true)]
            public partial class RealmModule;

            [SingletonService(Realm = typeof(RealmModule))]
            [Intercept(typeof(CountingInterceptor))]
            public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0020"));
    }

    /// <summary>
    /// The shape that happened to work, kept as a control: one convention module, not realm-only,
    /// so the unrealmed interception landed on it by luck rather than by rule.
    /// </summary>
    [VerifyTest]
    public void AConventionRegisteredClass_InAPlainModule_IsStillIntercepted()
    {
        var result = Run(
                """
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class ConventionModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IGreeter>().AsSingleton();
                    }
                }
                """
            )
            .AssertNoErrors();

        Verify(
            result.SourceContaining("ConventionModule.Interceptors").Contains("Greeter_Intercepted")
        );
    }

    /// <summary>
    /// A convention registering the class as itself rather than as its interface. Recorded because
    /// it is a separate reason interception can go missing from a convention-registered class, and
    /// telling the two apart matters: this one is about which service type was registered, not
    /// about which module the applicator landed on.
    /// </summary>
    [VerifyTest]
    public void AConventionRegisteringAsSelf_StillInterceptsTheInterface()
    {
        var result = Run(
                """
                [Intercept(typeof(CountingInterceptor))]
                public sealed class Greeter : IGreeter { public string Greet() => "hi"; }

                [DependencyModule]
                public partial class ConventionModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IGreeter>().AsSelf().AsSingleton();
                    }
                }
                """
            )
            .AssertNoErrors();

        Verify(
            result.SourceContaining("ConventionModule.Interceptors").Contains("Greeter_Intercepted")
        );
    }

    private static GeneratorResult Run(string body) => GeneratorTestHarness.Run(Preamble + body);
}

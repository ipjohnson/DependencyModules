using DependencyModules.Runtime;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Behavioural tests for convention registration.
/// </summary>
/// <remarks>
/// These compile, load and resolve rather than asserting on generated text. Asserting on the emitted
/// source passes happily while the wrong service type is registered, which is exactly the class of
/// mistake convention matching is most likely to make.
/// </remarks>
public partial class ConventionRegistrationTests
{
    private const string Preamble = """
        using System;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;

        namespace TestNamespace;

        """;

    private static GeneratedAssembly Compile(string source) =>
        GeneratedAssembly.Create(Preamble + source);

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    /// <summary>
    /// A convention candidate carrying an environment condition registers on the same terms the
    /// attribute path would.
    /// </summary>
    /// <remarks>
    /// A class with a service attribute is never a convention candidate, so a condition on a
    /// convention-matched class has no other route. Ignoring it would put a development-only
    /// service into production with nothing at the declaration saying why.
    /// </remarks>
    [VerifyTest]
    [Row("Development", 2)]
    [Row("Production", 1)]
    public void ConventionsHonourEnvironmentConditions(string environmentName, int expected)
    {
        const string source = """
            public interface IFoo { }

            public class AlwaysFoo : IFoo { }

            [IfEnvironment("Development")]
            public class DevOnlyFoo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(environmentName)
        );

        Verify(assembly.Descriptors("IFoo").Count == expected);
    }

    /// <summary>
    /// A condition declared on the convention itself, gating every match rather than being repeated
    /// on each class.
    /// </summary>
    [VerifyTest]
    [Row("Development", 2)]
    [Row("Production", 0)]
    public void ConventionsCarryTheirOwnEnvironmentCondition(string environmentName, int expected)
    {
        const string source = """
            public interface IFoo { }

            public class OneFoo : IFoo { }
            public class TwoFoo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().IfEnvironment("Development").AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(environmentName)
        );

        Verify(assembly.Descriptors("IFoo").Count == expected);
    }

    /// <summary>
    /// A condition on the convention and one on a matched class combine with <b>and</b>.
    /// </summary>
    /// <remarks>
    /// Letting either side win would mean one declaration silently discarding a condition written in
    /// the other. The environment pins its values, so a variable on the machine running this cannot
    /// reach REGION.
    /// </remarks>
    [VerifyTest]
    [Row("Development", "eu", 2)]
    [Row("Development", "us", 1)]
    [Row("Production", "eu", 0)]
    public void ConventionAndClassConditionsCombineWithAnd(
        string environmentName,
        string region,
        int expected
    )
    {
        const string source = """
            public interface IFoo { }

            public class PlainFoo : IFoo { }

            [IfEnvironmentValue("REGION", "eu")]
            public class EuFoo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().IfEnvironment("Development").AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(false, environmentName) { { "REGION", region } }
        );

        Verify(assembly.Descriptors("IFoo").Count == expected);
    }

    [VerifyTest]
    [Row("on", 1)]
    [Row("off", 0)]
    public void ConventionsCarryValueConditions(string flag, int expected)
    {
        const string source = """
            public interface IFoo { }

            public class OneFoo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().IfEnvironmentValue("FLAG", "on").AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(false, "Development") { { "FLAG", flag } }
        );

        Verify(assembly.Descriptors("IFoo").Count == expected);
    }

    [VerifyTest]
    [Row("Development", 0)]
    [Row("Production", 1)]
    public void ConventionsCarryNegatedConditions(string environmentName, int expected)
    {
        const string source = """
            public interface IFoo { }

            public class OneFoo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().IfNotEnvironment("Development").AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(environmentName)
        );

        Verify(assembly.Descriptors("IFoo").Count == expected);
    }

    /// <summary>
    /// Two conventions matching one class under different conditions stay in separate models.
    /// </summary>
    /// <remarks>
    /// One guard is emitted per ServiceModel. Merged into a single model, whichever condition set
    /// won would gate the other convention's registrations too — so IBar would vanish outside
    /// Development despite its convention saying nothing about the environment.
    /// </remarks>
    [VerifyTest]
    [Row("Development", 1, 1)]
    [Row("Production", 0, 1)]
    public void ConventionsWithDifferentConditionsDoNotShareAGuard(
        string environmentName,
        int expectedFoo,
        int expectedBar
    )
    {
        const string source = """
            public interface IFoo { }
            public interface IBar { }

            public class Both : IFoo, IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().IfEnvironment("Development").AsSingleton();
                    conventions.RegisterAll<IBar>().AsSingleton();
                }
            }
            """;

        var assembly = GeneratedAssembly.Create(
            Preamble + source,
            environment: new ModuleEnvironment(environmentName)
        );

        Verify(assembly.Descriptors("IFoo").Count == expectedFoo);
        Verify(assembly.Descriptors("IBar").Count == expectedBar);
    }

    /// <summary>
    /// A partial class whose parts each reach the scanned interface is one candidate, not two.
    /// </summary>
    /// <remarks>
    /// The candidate provider runs per declaration, so a two-part partial produced two candidate
    /// models with the same implementation type, and the ambiguity check — which groups by
    /// implementation type — read that as one class matched by two conventions. It reported DM0004
    /// and registered nothing. One convention, named twice.
    /// </remarks>
    [VerifyTest]
    public void APartialClassReachingTheServiceFromTwoPartsIsNotAmbiguous()
    {
        const string source = """
            public interface IFoo { }

            public abstract class FooBase : IFoo { }

            public partial class Foo : IFoo { }
            public partial class Foo : FooBase { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton().IncludeBaseClasses();
                }
            }
            """;

        Verify(!Run(source).GeneratorDiagnostics.Any(d => d.Id == "DM0004"));

        var assembly = Compile(source);

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Foo"));
    }

    /// <summary>
    /// Two parts declaring different interfaces that both reach the scanned one is still one class.
    /// </summary>
    [VerifyTest]
    public void APartialClassDeclaringTheServiceTwiceIsNotAmbiguous()
    {
        const string source = """
            public interface IFoo { }
            public interface IFooPrime : IFoo { }

            public partial class Foo : IFoo { }
            public partial class Foo : IFooPrime { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        Verify(!Run(source).GeneratorDiagnostics.Any(d => d.Id == "DM0004"));

        Single(Compile(source).Descriptors("IFoo"));
    }

    /// <summary>
    /// Declaring the interface in any part is a declared match, so the base-class opt-in is not
    /// needed even when another part reaches the same interface through a base class.
    /// </summary>
    [VerifyTest]
    public void APartDeclaringTheServiceMakesItADeclaredMatch()
    {
        const string source = """
            public interface IFoo { }

            public abstract class FooBase : IFoo { }

            public partial class Foo : IFoo { }
            public partial class Foo : FooBase { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        Single(Compile(source).Descriptors("IFoo"));
    }

    /// <summary>
    /// The constructor can be declared in a different part from the one that names the interface.
    /// </summary>
    /// <remarks>
    /// Each part reports its own constructors, and a part that declares none reports an empty
    /// parameter list. Taking the first part's would emit a call to a constructor the type does not
    /// have, which is a CS error inside generated code.
    /// </remarks>
    [VerifyTest]
    public void TheConstructorMayBeDeclaredInAnotherPart()
    {
        const string source = """
            public interface IDep { }

            [SingletonService]
            public class Dep : IDep { }

            public interface IFoo { }

            public abstract class FooBase : IFoo { }

            public partial class Foo : IFoo { }

            public partial class Foo : FooBase {
                public Foo(IDep dep) { Dep = dep; }
                public IDep Dep { get; }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        var assembly = Compile(source);

        Verify(assembly.ResolveRequired("IFoo").GetType() == assembly.Type("Foo"));
    }

    /// <summary>
    /// A type filling two roles registers as both. This is the ordinary shape of a MediatR handler,
    /// and the two registrations are independently predictable from reading the module.
    /// </summary>
    [VerifyTest]
    public void ATypeMatchedThroughDifferentInterfacesRegistersAsBoth()
    {
        const string source = """
            public interface IFoo { }
            public interface IBar { }

            public partial class Thing : IFoo { }
            public partial class Thing : IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                    conventions.RegisterAll<IBar>().AsSingleton();
                }
            }
            """;

        Verify(!Run(source).GeneratorDiagnostics.Any(d => d.Id == "DM0004"));

        var assembly = Compile(source);

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Thing"));
        Verify(assembly.Descriptor("IBar").ImplementationType == assembly.Type("Thing"));
    }

    /// <summary>
    /// Each role keeps its own lifetime, and two singletons of one implementation registered under
    /// different service types are two instances — which is what Scrutor and MediatR both produce.
    /// Sharing one instance is <c>AsSelfWithInterfaces</c>, and it is opt-in.
    /// </summary>
    [VerifyTest]
    public void EachRoleKeepsItsOwnLifetimeAndItsOwnInstance()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public interface IBar { }

            public class Thing : IFoo, IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                    conventions.RegisterAll<IBar>().AsScoped();
                }
            }
            """
        );

        Verify(assembly.Descriptor("IFoo").Lifetime == ServiceLifetime.Singleton);
        Verify(assembly.Descriptor("IBar").Lifetime == ServiceLifetime.Scoped);

        var provider = assembly.BuildProvider();

        Verify(
            !ReferenceEquals(
                provider.GetService(assembly.Type("IFoo")),
                provider.GetService(assembly.Type("IBar"))
            )
        );
    }

    /// <summary>
    /// A handler covering several messages registers against every closing it implements.
    /// </summary>
    /// <remarks>
    /// The MediatR shape. Registering only the first closing left the second silently unregistered:
    /// green build, no diagnostic, and an event that never fires.
    /// </remarks>
    [VerifyTest]
    public void OneConventionRegistersEveryClosingACandidateImplements()
    {
        var assembly = Compile(
            """
            public interface INotificationHandler<T> { }

            public class OrderPlaced { }
            public class OrderShipped { }

            public class OrderEvents : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(INotificationHandler<>)).AsTransient();
                }
            }
            """
        );

        var handlerType = assembly.Type("INotificationHandler`1");

        var placed = handlerType.MakeGenericType(assembly.Type("OrderPlaced"));
        var shipped = handlerType.MakeGenericType(assembly.Type("OrderShipped"));

        var provider = assembly.BuildProvider();

        Verify(provider.GetService(placed)?.GetType() == assembly.Type("OrderEvents"));
        Verify(provider.GetService(shipped)?.GetType() == assembly.Type("OrderEvents"));
    }

    /// <summary>
    /// Several closings on one type are one implementation with several registrations, not several
    /// implementations — the shape the attribute path produces and the writer relies on.
    /// </summary>
    [VerifyTest]
    public void SeveralClosingsProduceOneImplementationWithSeveralRegistrations()
    {
        var assembly = Compile(
            """
            public interface IHandler<T> { }

            public class A { }
            public class B { }

            public class Both : IHandler<A>, IHandler<B> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<>)).AsTransient();
                }
            }
            """
        );

        var registered = assembly
            .Services.Where(d => d.ImplementationType == assembly.Type("Both"))
            .ToArray();

        Verify(registered.Length == 2);
    }

    private const string Marker = """
        public class HandlerAttribute : System.Attribute { }
        public class LegacyAttribute : System.Attribute { }

        public interface IFoo { }

        [Handler]
        public class Marked : IFoo { }

        public class Unmarked : IFoo { }

        [Handler]
        [Legacy]
        public class MarkedLegacy : IFoo { }
        """;

    [VerifyTest]
    public void WithAttributeLimitsMatchesToTypesCarryingIt()
    {
        var assembly = Compile(
            Marker
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IFoo>().WithAttribute<HandlerAttribute>().AsSingleton();
                    }
                }
                """
        );

        var implementations = assembly
            .Descriptors("IFoo")
            .Select(d => d.ImplementationType)
            .ToArray();

        Verify(implementations.Length == 2);
        Verify(implementations.Contains(assembly.Type("Marked")));
        Verify(implementations.Contains(assembly.Type("MarkedLegacy")));
        Verify(!implementations.Contains(assembly.Type("Unmarked")));
    }

    [VerifyTest]
    public void WithoutAttributeExcludesTypesCarryingIt()
    {
        var assembly = Compile(
            Marker
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IFoo>().WithoutAttribute<LegacyAttribute>().AsSingleton();
                    }
                }
                """
        );

        var implementations = assembly
            .Descriptors("IFoo")
            .Select(d => d.ImplementationType)
            .ToArray();

        Verify(implementations.Length == 2);
        Verify(!implementations.Contains(assembly.Type("MarkedLegacy")));
    }

    [VerifyTest]
    public void AttributeFiltersCombineWithAnd()
    {
        var assembly = Compile(
            Marker
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IFoo>()
                            .WithAttribute<HandlerAttribute>()
                            .WithoutAttribute<LegacyAttribute>()
                            .AsSingleton();
                    }
                }
                """
        );

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Marked"));
    }

    /// <summary>
    /// Resolved rather than matched on how it was written, so the qualified form counts.
    /// </summary>
    [VerifyTest]
    public void ANamespaceQualifiedAttributeStillMatches()
    {
        var assembly = Compile(
            Marker
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll<IFoo>()
                            .WithAttribute<TestNamespace.HandlerAttribute>()
                            .AsSingleton();
                    }
                }
                """
        );

        Verify(assembly.Descriptors("IFoo").Count == 2);
    }

    [VerifyTest]
    [Row("*Repository", 2)]
    [Row("Order*", 2)]
    [Row("Order?epository", 1)]
    [Row("TestNamespace.*Repository", 2)]
    [Row("*repository", 0)]
    public void WithNameMatchesTheGlob(string pattern, int expected)
    {
        var result = Run(
            $$"""
            public class OrderRepository { }
            public class UserRepository { }
            public class OrderService { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().WithName("{{pattern}}").AsSelf().AsScoped();
                }
            }
            """
        );

        // A pattern matching nothing is DM0005 rather than silence.
        if (expected == 0)
        {
            Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0005"));

            return;
        }

        var assembly = GeneratedAssembly.Create(
            Preamble
                + $$"""
                public class OrderRepository { }
                public class UserRepository { }
                public class OrderService { }

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll().WithName("{{pattern}}").AsSelf().AsScoped();
                    }
                }
                """
        );

        Verify(
            assembly.Services.Count(d => d.ImplementationType?.Namespace == "TestNamespace")
                == expected
        );
    }

    [VerifyTest]
    public void AsRegistersEveryMatchAsOneNamedService()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public interface IMarker { }

            public class Foo : IFoo, IMarker { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().As<IMarker>().AsSingleton();
                }
            }
            """
        );

        Verify(assembly.Descriptors("IFoo").Count == 0);
        Verify(assembly.Descriptor("IMarker").ImplementationType == assembly.Type("Foo"));
    }

    [VerifyTest]
    public void AsMatchingInterfaceRegistersFooAsIFoo()
    {
        var assembly = Compile(
            """
            public interface IMarker { }
            public interface IFoo : IMarker { }
            public interface IBar : IMarker { }

            public class Foo : IFoo { }
            public class Bar : IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IMarker>().AsMatchingInterface().AsSingleton();
                }
            }
            """
        );

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Foo"));
        Verify(assembly.Descriptor("IBar").ImplementationType == assembly.Type("Bar"));
        Verify(assembly.Descriptors("IMarker").Count == 0);
    }

    /// <summary>
    /// The expansion does not cross-wire BCL interfaces.
    /// </summary>
    /// <remarks>
    /// Any type whose base implements IDisposable would otherwise become resolvable as IDisposable,
    /// which is never what "register this as its interfaces" means.
    /// </remarks>
    [VerifyTest]
    public void AsSelfWithInterfacesSkipsSystemInterfaces()
    {
        var assembly = Compile(
            """
            public interface IMarker { }

            public abstract class ThingBase : System.IDisposable, IMarker {
                public void Dispose() { }
            }

            public class Thing : ThingBase { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IMarker>().AsSelfWithInterfaces().AsSingleton().IncludeBaseClasses();
                }
            }
            """
        );

        Single(assembly.Descriptors("IMarker"));

        Verify(!assembly.Services.Any(d => d.ServiceType == typeof(IDisposable)));
    }

    [VerifyTest]
    public void AsSelfWithInterfacesSkipsGenericSystemInterfaces()
    {
        var assembly = Compile(
            """
            public interface IRule { }
            public interface IValidator<T> { }

            public abstract class ValidatorBase<T> : IValidator<T>, System.Collections.Generic.IEnumerable<IRule> {
                public System.Collections.Generic.IEnumerator<IRule> GetEnumerator() => null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
            }

            public class Foo { }

            public class FooValidator : ValidatorBase<Foo> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IValidator<>)).AsSelfWithInterfaces().AsScoped().IncludeBaseClasses();
                }
            }
            """
        );

        // The validator interface survives; the enumerable ones do not.
        Single(assembly.Services, d => d.ServiceType.Name == "IValidator`1");

        Verify(!assembly.Services.Any(d => d.ServiceType.Namespace?.StartsWith("System") == true));
    }

    /// <summary>
    /// The filter is scoped to the expansion. A service type the developer named is honoured
    /// whatever namespace it lives in — refusing that is a different kind of wrong.
    /// </summary>
    [VerifyTest]
    public void ANamedSystemServiceTypeIsStillRegistered()
    {
        var assembly = Compile(
            """
            public class Closeable : System.IDisposable {
                public void Dispose() { }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<System.IDisposable>().AsSingleton();
                }
            }
            """
        );

        Single(assembly.Services, d => d.ServiceType == typeof(IDisposable));
    }

    /// <summary>
    /// Filtering everything away degrades to AsSelf rather than to nothing.
    /// </summary>
    [VerifyTest]
    public void ATypeReachingOnlySystemInterfacesRegistersItself()
    {
        var assembly = Compile(
            """
            public interface IMarker { }

            public abstract class OnlySystemBase : System.IDisposable, IMarker {
                public void Dispose() { }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IMarker>().AsSelf().AsSingleton().IncludeBaseClasses();
                }
            }
            """
        );

        // Nothing to assert beyond the module compiling and registering something; the shape that
        // matters is covered by AsSelfWithInterfacesSkipsSystemInterfaces.
        Verify(assembly.Services.Any());
    }

    private const string Validators = """
        public interface IRule { }
        public interface IValidator { }
        public interface IValidator<T> : IValidator { }

        public abstract class AbstractValidator<T> : IValidator<T>, System.Collections.Generic.IEnumerable<IRule> {
            public System.Collections.Generic.IEnumerator<IRule> GetEnumerator() => null!;
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
        }

        public class Foo { }

        public class FooValidator : AbstractValidator<Foo> { }
        """;

    /// <summary>
    /// The FluentValidation shape: registered as the matched interface and as the concrete type.
    /// </summary>
    [VerifyTest]
    public void AlsoAsSelfRegistersTheInterfaceAndTheType()
    {
        var assembly = Compile(
            Validators
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll(typeof(IValidator<>)).IncludeBaseClasses().AlsoAsSelf().AsScoped();
                    }
                }
                """
        );

        Verify(assembly.Services.Any(d => d.ServiceType.Name == "IValidator`1"));
        Verify(assembly.Services.Any(d => d.ServiceType == assembly.Type("FooValidator")));
    }

    /// <summary>
    /// Cross-wired, so both routes give one instance per scope. FluentValidation registers the pair
    /// independently and hands you two; this is a deliberate difference.
    /// </summary>
    [VerifyTest]
    public void AlsoAsSelfSharesOneInstance()
    {
        var assembly = Compile(
            Validators
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll(typeof(IValidator<>)).IncludeBaseClasses().AlsoAsSelf().AsScoped();
                    }
                }
                """
        );

        var provider = assembly.BuildProvider();
        using var scope = provider.CreateScope();

        var validatorInterface = assembly
            .Services.First(d => d.ServiceType.Name == "IValidator`1")
            .ServiceType;

        Verify(
            ReferenceEquals(
                scope.ServiceProvider.GetService(validatorInterface),
                scope.ServiceProvider.GetService(assembly.Type("FooValidator"))
            )
        );
    }

    /// <summary>
    /// Only the interfaces the convention matched, not everything the type can reach.
    /// </summary>
    [VerifyTest]
    public void AlsoAsSelfDoesNotPullInUnmatchedInterfaces()
    {
        var assembly = Compile(
            Validators
                + """

                [DependencyModule]
                public partial class TestModule : IConventionModule {
                    void IConventionModule.Conventions(IConventionDefinitions conventions) {
                        conventions.RegisterAll(typeof(IValidator<>)).IncludeBaseClasses().AlsoAsSelf().AsScoped();
                    }
                }
                """
        );

        // IValidator and the enumerable interfaces are reachable but were not matched.
        Verify(!assembly.Services.Any(d => d.ServiceType == assembly.Type("IValidator")));
        Verify(!assembly.Services.Any(d => d.ServiceType.Namespace?.StartsWith("System") == true));
    }

    [VerifyTest]
    public void AsSelfAndAlsoAsSelfTogetherIsRefused()
    {
        var result = Run(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSelf().AlsoAsSelf().AsSingleton();
                }
            }
            """
        );

        Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");
    }

    /// <summary>
    /// A handler closing two messages asks for the self registration twice and means it once. One
    /// convention repeating itself is not an ambiguity.
    /// </summary>
    [VerifyTest]
    public void AlsoAsSelfRegistersTheTypeOnceAcrossSeveralClosings()
    {
        var result = Run(
            """
            public interface IHandler<T> { }

            public class A { }
            public class B { }

            public class Both : IHandler<A>, IHandler<B> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<>)).AlsoAsSelf().AsScoped();
                }
            }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0004"));

        var assembly = Compile(
            """
            public interface IHandler<T> { }

            public class A { }
            public class B { }

            public class Both : IHandler<A>, IHandler<B> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<>)).AlsoAsSelf().AsScoped();
                }
            }
            """
        );

        Single(assembly.Services, d => d.ServiceType == assembly.Type("Both"));
        Verify(assembly.Services.Count(d => d.ServiceType.Name == "IHandler`1") == 2);
    }

    [VerifyTest]
    public void UsingChoosesHowTheRegistrationIsAdded()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public class FooOne : IFoo { }
            public class FooTwo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton().Using(RegistrationType.Try);
                }
            }
            """
        );

        // Try registers the service type once and skips the second match.
        Single(assembly.Descriptors("IFoo"));
    }

    [VerifyTest]
    public void WithKeyRegistersUnderAServiceKey()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton().WithKey("primary");
                }
            }
            """
        );

        var descriptor = assembly.Descriptor("IFoo");

        Verify(descriptor.IsKeyedService);
        Verify(Equals(descriptor.ServiceKey, "primary"));

        Verify(
            assembly
                .BuildProvider()
                .GetRequiredKeyedService(assembly.Type("IFoo"), "primary")
                .GetType() == assembly.Type("Foo")
        );
    }

    /// <summary>
    /// The case DM0004 exists for: one service type claimed twice, so one lifetime has to win and
    /// the source does not say which.
    /// </summary>
    [VerifyTest]
    public void TwoConventionsRegisteringOneServiceTypeIsAmbiguous()
    {
        var result = Run(
            """
            public interface IFoo { }
            public interface IFooPrime : IFoo { }

            public class Thing : IFooPrime { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsScoped();
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0004");

        Verify(diagnostic.GetMessage().Contains("different lifetimes"));
        Verify(diagnostic.GetMessage().Contains("IFoo"));
    }

    /// <summary>
    /// Equal lifetimes are still an error. The outcome is predictable, but the declaration is
    /// redundant and collapsing it silently is the failure mode this codebase avoids.
    /// </summary>
    [VerifyTest]
    public void ADuplicatedConventionIsAmbiguousEvenWithEqualLifetimes()
    {
        var result = Run(
            """
            public interface IFoo { }

            public class Thing : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0004");

        Verify(diagnostic.GetMessage().Contains("duplicated"));
    }

    [VerifyTest]
    public void AsSelfRegistersTheConcreteTypeRatherThanTheService()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSelf().AsSingleton();
                }
            }
            """
        );

        Verify(assembly.Descriptors("IFoo").Count == 0);
        Single(assembly.Descriptors("Foo"));
    }

    /// <summary>
    /// AsSelfWithInterfaces promises one instance reachable through the type and every interface,
    /// which is why it emits the cross-wire shape rather than two independent registrations.
    /// </summary>
    [VerifyTest]
    public void AsSelfWithInterfacesSharesOneInstance()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public interface IBar { }
            public class Foo : IFoo, IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSelfWithInterfaces().AsSingleton();
                }
            }
            """
        );

        var provider = assembly.BuildProvider();

        var asFoo = provider.GetService(assembly.Type("Foo"));
        var asIFoo = provider.GetService(assembly.Type("IFoo"));
        var asIBar = provider.GetService(assembly.Type("IBar"));

        Verify(asFoo is not null);
        Verify(ReferenceEquals(asFoo, asIFoo));
        Verify(ReferenceEquals(asFoo, asIBar));
    }

    /// <summary>
    /// The hole this closes: a concrete class implementing nothing, selected by namespace.
    /// </summary>
    [VerifyTest]
    public void AConcreteTypeWithNoInterfaceRegistersByNamespace()
    {
        var assembly = Compile(
            """
            public class OrderCalculator { }
            public class OrderValidator { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().InNamespaceOf<OrderCalculator>().AsSelf().AsScoped();
                }
            }
            """
        );

        Single(assembly.Descriptors("OrderCalculator"));
        Single(assembly.Descriptors("OrderValidator"));
    }

    [VerifyTest]
    public void NamespaceFiltersNarrowAnAssignabilityConvention()
    {
        var result = Run(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().InNamespaces("SomewhereElse").AsSingleton();
                }
            }
            """
        );

        // Filtered out entirely, which is DM0005 rather than silence.
        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0005"));
    }

    [VerifyTest]
    public void NotInNamespacesExcludesAfterInclusions()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().NotInNamespaces("TestNamespace").AsSingleton();
                }
            }
            """
        );

        Verify(assembly.Descriptors("IFoo").Count == 0);
    }

    [VerifyTest]
    public void RegisterAllWithNoServiceTypeNeedsAShape()
    {
        var result = Run(
            """
            public class Thing { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().InNamespaceOf<Thing>().AsScoped();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Verify(diagnostic.GetMessage().Contains("AsSelf"));
    }

    /// <summary>
    /// Without a filter it would match every class in the compilation, so it is refused.
    /// </summary>
    [VerifyTest]
    public void RegisterAllWithNoServiceTypeAndNoFilterIsRefused()
    {
        var result = Run(
            """
            public class Thing { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().AsSelf().AsScoped();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Verify(diagnostic.GetMessage().Contains("every class"));
    }

    [VerifyTest]
    public void RegistersEveryTypeDeclaringTheServiceInterface()
    {
        var assembly = Compile(
            """
            public interface IFoo { }

            public class FooOne : IFoo { }
            public class FooTwo : IFoo { }
            public class NotAFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var descriptors = assembly.Descriptors("IFoo");

        Verify(descriptors.Count == 2);
        foreach (var d in descriptors)
        {
            Verify(d.Lifetime == ServiceLifetime.Singleton);
        }
        Verify(descriptors.Any(d => d.ImplementationType == assembly.Type("FooOne")));
        Verify(descriptors.Any(d => d.ImplementationType == assembly.Type("FooTwo")));
    }

    [VerifyTest]
    public void RegisteredServiceResolves()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsScoped();
                }
            }
            """
        );

        Verify(assembly.ResolveRequired("IFoo").GetType() == assembly.Type("Foo"));
        Verify(assembly.Descriptor("IFoo").Lifetime == ServiceLifetime.Scoped);
    }

    /// <summary>
    /// An interface declaring that it extends another is a deliberate statement of substitutability,
    /// so a convention naming the base interface matches by declaration.
    /// </summary>
    [VerifyTest]
    public void MatchesThroughInterfaceInheritance()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public interface IFooPrime : IFoo { }

            public class Thing : IFooPrime { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Thing"));
    }

    /// <summary>
    /// Extending a class is a statement about implementation reuse rather than about the contract,
    /// and every subclass added later would join the convention with nobody revisiting it. So it
    /// takes an explicit opt-in.
    /// </summary>
    [VerifyTest]
    public void DoesNotMatchThroughABaseClassByDefault()
    {
        var result = Run(
            """
            public interface IFoo { }
            public abstract class ThingBase : IFoo { }
            public class Thing : ThingBase { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        // Nothing matched, which is DM0005 rather than silence.
        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0005"));
    }

    [VerifyTest]
    public void MatchesThroughABaseClassWhenAskedTo()
    {
        var assembly = Compile(
            """
            public interface IFoo { }
            public abstract class ThingBase : IFoo { }
            public class Thing : ThingBase { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton().IncludeBaseClasses();
                }
            }
            """
        );

        Verify(assembly.Descriptor("IFoo").ImplementationType == assembly.Type("Thing"));
    }

    /// <summary>
    /// The make-or-break behaviour for open generics: the match registers the closed construction it
    /// actually implements, not the open definition.
    /// </summary>
    [VerifyTest]
    public void ClosesAnOpenGenericAgainstEachImplementation()
    {
        var assembly = Compile(
            """
            public interface IHandler<TIn, TOut> { }

            public class CreateOrder { }
            public class OrderId { }
            public class Rename { }

            public class CreateOrderHandler : IHandler<CreateOrder, OrderId> { }
            public class RenameHandler : IHandler<Rename, OrderId> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>)).AsTransient();
                }
            }
            """
        );

        var handler = assembly.Type("IHandler`2");

        var createOrder = handler.MakeGenericType(
            assembly.Type("CreateOrder"),
            assembly.Type("OrderId")
        );
        var rename = handler.MakeGenericType(assembly.Type("Rename"), assembly.Type("OrderId"));

        var provider = assembly.BuildProvider();

        Verify(provider.GetService(createOrder)?.GetType() == assembly.Type("CreateOrderHandler"));
        Verify(provider.GetService(rename)?.GetType() == assembly.Type("RenameHandler"));
    }

    /// <summary>
    /// A closed convention registers only that construction. Without this,
    /// RegisterAll&lt;IHandler&lt;A,B&gt;&gt;() would pick up every other closing too.
    /// </summary>
    [VerifyTest]
    public void AClosedGenericConventionMatchesOnlyThatConstruction()
    {
        var assembly = Compile(
            """
            public interface IRepo<T> { }
            public class IntRepo : IRepo<int> { }
            public class StringRepo : IRepo<string> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IRepo<int>>().AsSingleton();
                }
            }
            """
        );

        var repo = assembly.Type("IRepo`1");
        var provider = assembly.BuildProvider();

        Verify(
            provider.GetService(repo.MakeGenericType(typeof(int)))?.GetType()
                == assembly.Type("IntRepo")
        );

        Verify(provider.GetService(repo.MakeGenericType(typeof(string))) is null);
    }

    [VerifyTest]
    public void AnExplicitServiceAttributeBeatsTheConvention()
    {
        var assembly = Compile(
            """
            public interface IFoo { }

            [SingletonService]
            public class Attributed : IFoo { }

            public class ByConvention : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsTransient();
                }
            }
            """
        );

        var descriptors = assembly.Descriptors("IFoo");

        // The attributed type is registered once, by its attribute, at the lifetime the attribute
        // declared — not a second time at the convention's lifetime.
        var attributed = descriptors
            .Where(d => d.ImplementationType == assembly.Type("Attributed"))
            .ToArray();

        Single(attributed);
        Verify(attributed[0].Lifetime == ServiceLifetime.Singleton);

        var byConvention = descriptors
            .Where(d => d.ImplementationType == assembly.Type("ByConvention"))
            .ToArray();

        Single(byConvention);
        Verify(byConvention[0].Lifetime == ServiceLifetime.Transient);
    }

    [VerifyTest]
    public void OmittingTheLifetimeIsRefused()
    {
        var result = Run(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Verify(diagnostic.GetMessage().Contains("lifetime"));
    }

    /// <summary>
    /// The body is configuration read at compile time, so anything outside the closed set of calls
    /// has no meaning. Refused rather than skipped: dropping it would lose registrations while the
    /// build stayed green.
    /// </summary>
    [VerifyTest]
    [Row("foreach (var t in new Type[0]) { conventions.RegisterAll<IFoo>().AsSingleton(); }")]
    [Row("if (DateTime.Now.Day > 1) { conventions.RegisterAll<IFoo>().AsSingleton(); }")]
    [Row("var x = 5;")]
    [Row("Helper(conventions);")]
    [Row("conventions.RegisterAll<IFoo>().AsSingleton().AsScoped();")]
    public void UnreadableStatementsAreRefused(string statement)
    {
        var result = Run(
            $$"""
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    {{statement}}
                }

                private static void Helper(IConventionDefinitions c) { }
            }
            """
        );

        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0009"));
    }

    // TwoConventionsMatchingOneTypeIsAmbiguous lived here. It asserted DM0004 for a type matched
    // through two different interfaces, which is now a legal two-role registration; see
    // EachRoleKeepsItsOwnLifetimeAndItsOwnInstance, which covers the same source and asserts what it
    // registers rather than that it refuses. TwoConventionsRegisteringOneServiceTypeIsAmbiguous
    // keeps DM0004 honest for the case it exists for.

    [VerifyTest]
    public void AConcreteTypeWithNoAccessibleConstructorIsReported()
    {
        var result = Run(
            """
            public interface IFoo { }

            public class Hidden : IFoo {
                private Hidden() { }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");

        Verify(diagnostic.GetMessage().Contains("Hidden"));
    }

    /// <summary>
    /// The class carries no attribute saying it is registered, so this is the only thing at the
    /// declaration that explains why it is in the container.
    /// </summary>
    [VerifyTest]
    public void ReportsWhatEachClassIsExposedAs()
    {
        var result = Run(
            """
            public interface IFoo { }
            public interface IFooPrime : IFoo { }

            public class Direct : IFoo { }
            public class Indirect : IFooPrime { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var exposures = result.GeneratorDiagnostics.Where(d => d.Id == "DM0010").ToArray();

        Verify(exposures.Length == 2);
        foreach (var d in exposures)
        {
            Verify(d.Severity == DiagnosticSeverity.Info);
        }

        // The indirect match names the hop, which is what keeps it from reading as luck.
        Verify(exposures.Any(d => d.GetMessage() == "Exposed as IFoo in TestModule"));
        Verify(
            exposures.Any(d => d.GetMessage() == "Exposed as IFoo in TestModule (via IFooPrime)")
        );
    }

    /// <summary>
    /// The location is what makes DM0010 useful, and it is the one thing that cannot be stored
    /// directly in an incremental model — a Location pins its SyntaxTree — so it is rebuilt from
    /// primitives at output. This asserts the rebuild lands on the right line.
    /// </summary>
    [VerifyTest]
    public void ExposureIsReportedOnTheClassItself()
    {
        const string body = """
            public interface IFoo { }

            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        var result = Run(body);

        var exposure = Single(result.GeneratorDiagnostics.Where(d => d.Id == "DM0010"));
        var span = exposure.Location.GetLineSpan();

        Verify(span.Path.EndsWith("Test.cs"));

        var lines = (Preamble + body).Replace("\r\n", "\n").Split('\n');

        Verify(lines[span.StartLinePosition.Line].Contains("class Foo"));
    }

    /// <summary>
    /// A type implementing IConventionModule that is not a module registers nothing at all, so it is
    /// reported rather than left to fail silently.
    /// </summary>
    [VerifyTest]
    public void ConventionsOnANonModuleAreReported()
    {
        var result = Run(
            """
            public interface IFoo { }
            public class Foo : IFoo { }

            [DependencyModule]
            public partial class TestModule { }

            public class NotAModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Verify(diagnostic.GetMessage().Contains("NotAModule"));
    }

    [VerifyTest]
    public void EditingAnUnrelatedMethodBodyReusesTheCachedOutput()
    {
        const string template = """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IFoo { }

            public class Foo : IFoo {
                public int Compute() { return VALUE; }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                }
            }
            """;

        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = template.Replace("VALUE", "1") },
            new Dictionary<string, string> { ["Test.cs"] = template.Replace("VALUE", "2") }
        );

        Verify(result.SecondRun.Count == result.FirstRun.Count);
        foreach (var (hintName, source) in result.FirstRun)
        {
            Verify(result.SecondRun.TryGetValue(hintName, out var second) && second == source);
        }
        Verify(
            result.AllOutputsCached,
            "editing a method body cannot change any registration, so every output should be cached"
        );
    }
}

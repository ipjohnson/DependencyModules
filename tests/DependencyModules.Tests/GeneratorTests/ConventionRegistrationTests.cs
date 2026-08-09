using DependencyModules.Runtime;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Behavioural tests for convention registration.
/// </summary>
/// <remarks>
/// These compile, load and resolve rather than asserting on generated text. Asserting on the emitted
/// source passes happily while the wrong service type is registered, which is exactly the class of
/// mistake convention matching is most likely to make.
/// </remarks>
public class ConventionRegistrationTests {

    private const string Preamble =
        """
        using System;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Conventions;

        namespace TestNamespace;

        """;

    private static GeneratedAssembly Compile(string source) =>
        GeneratedAssembly.Create(Preamble + source, withConventions: true);

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source, withConventions: true);

    /// <summary>
    /// A convention candidate carrying an environment condition registers on the same terms the
    /// attribute path would.
    /// </summary>
    /// <remarks>
    /// A class with a service attribute is never a convention candidate, so a condition on a
    /// convention-matched class has no other route. Ignoring it would put a development-only
    /// service into production with nothing at the declaration saying why.
    /// </remarks>
    [Theory]
    [InlineData("Development", 2)]
    [InlineData("Production", 1)]
    public void ConventionsHonourEnvironmentConditions(string environmentName, int expected) {
        const string source =
            """
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
            withConventions: true,
            environment: new ModuleEnvironment(environmentName));

        Assert.Equal(expected, assembly.Descriptors("IFoo").Count);
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
    [Fact]
    public void APartialClassReachingTheServiceFromTwoPartsIsNotAmbiguous() {
        const string source =
            """
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

        Assert.DoesNotContain(Run(source).GeneratorDiagnostics, d => d.Id == "DM0004");

        var assembly = Compile(source);

        Assert.Equal(assembly.Type("Foo"), assembly.Descriptor("IFoo").ImplementationType);
    }

    /// <summary>
    /// Two parts declaring different interfaces that both reach the scanned one is still one class.
    /// </summary>
    [Fact]
    public void APartialClassDeclaringTheServiceTwiceIsNotAmbiguous() {
        const string source =
            """
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

        Assert.DoesNotContain(Run(source).GeneratorDiagnostics, d => d.Id == "DM0004");

        Assert.Single(Compile(source).Descriptors("IFoo"));
    }

    /// <summary>
    /// Declaring the interface in any part is a declared match, so the base-class opt-in is not
    /// needed even when another part reaches the same interface through a base class.
    /// </summary>
    [Fact]
    public void APartDeclaringTheServiceMakesItADeclaredMatch() {
        const string source =
            """
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

        Assert.Single(Compile(source).Descriptors("IFoo"));
    }

    /// <summary>
    /// The constructor can be declared in a different part from the one that names the interface.
    /// </summary>
    /// <remarks>
    /// Each part reports its own constructors, and a part that declares none reports an empty
    /// parameter list. Taking the first part's would emit a call to a constructor the type does not
    /// have, which is a CS error inside generated code.
    /// </remarks>
    [Fact]
    public void TheConstructorMayBeDeclaredInAnotherPart() {
        const string source =
            """
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

        Assert.IsType(assembly.Type("Foo"), assembly.ResolveRequired("IFoo"));
    }

    /// <summary>
    /// A type filling two roles registers as both. This is the ordinary shape of a MediatR handler,
    /// and the two registrations are independently predictable from reading the module.
    /// </summary>
    [Fact]
    public void ATypeMatchedThroughDifferentInterfacesRegistersAsBoth() {
        const string source =
            """
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

        Assert.DoesNotContain(Run(source).GeneratorDiagnostics, d => d.Id == "DM0004");

        var assembly = Compile(source);

        Assert.Equal(assembly.Type("Thing"), assembly.Descriptor("IFoo").ImplementationType);
        Assert.Equal(assembly.Type("Thing"), assembly.Descriptor("IBar").ImplementationType);
    }

    /// <summary>
    /// Each role keeps its own lifetime, and two singletons of one implementation registered under
    /// different service types are two instances — which is what Scrutor and MediatR both produce.
    /// Sharing one instance is <c>AsSelfWithInterfaces</c>, and it is opt-in.
    /// </summary>
    [Fact]
    public void EachRoleKeepsItsOwnLifetimeAndItsOwnInstance() {
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
            """);

        Assert.Equal(ServiceLifetime.Singleton, assembly.Descriptor("IFoo").Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, assembly.Descriptor("IBar").Lifetime);

        var provider = assembly.BuildProvider();

        Assert.NotSame(
            provider.GetService(assembly.Type("IFoo")),
            provider.GetService(assembly.Type("IBar")));
    }

    /// <summary>
    /// A handler covering several messages registers against every closing it implements.
    /// </summary>
    /// <remarks>
    /// The MediatR shape. Registering only the first closing left the second silently unregistered:
    /// green build, no diagnostic, and an event that never fires.
    /// </remarks>
    [Fact]
    public void OneConventionRegistersEveryClosingACandidateImplements() {
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
            """);

        var handlerType = assembly.Type("INotificationHandler`1");

        var placed = handlerType.MakeGenericType(assembly.Type("OrderPlaced"));
        var shipped = handlerType.MakeGenericType(assembly.Type("OrderShipped"));

        var provider = assembly.BuildProvider();

        Assert.IsType(assembly.Type("OrderEvents"), provider.GetService(placed));
        Assert.IsType(assembly.Type("OrderEvents"), provider.GetService(shipped));
    }

    /// <summary>
    /// Several closings on one type are one implementation with several registrations, not several
    /// implementations — the shape the attribute path produces and the writer relies on.
    /// </summary>
    [Fact]
    public void SeveralClosingsProduceOneImplementationWithSeveralRegistrations() {
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
            """);

        var registered = assembly.Services
            .Where(d => d.ImplementationType == assembly.Type("Both"))
            .ToArray();

        Assert.Equal(2, registered.Length);
    }

    [Fact]
    public void UsingChoosesHowTheRegistrationIsAdded() {
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
            """);

        // Try registers the service type once and skips the second match.
        Assert.Single(assembly.Descriptors("IFoo"));
    }

    [Fact]
    public void WithKeyRegistersUnderAServiceKey() {
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
            """);

        var descriptor = assembly.Descriptor("IFoo");

        Assert.True(descriptor.IsKeyedService);
        Assert.Equal("primary", descriptor.ServiceKey);

        Assert.IsType(
            assembly.Type("Foo"),
            assembly.BuildProvider().GetRequiredKeyedService(assembly.Type("IFoo"), "primary"));
    }

    /// <summary>
    /// The case DM0004 exists for: one service type claimed twice, so one lifetime has to win and
    /// the source does not say which.
    /// </summary>
    [Fact]
    public void TwoConventionsRegisteringOneServiceTypeIsAmbiguous() {
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
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0004");

        Assert.Contains("different lifetimes", diagnostic.GetMessage());
        Assert.Contains("IFoo", diagnostic.GetMessage());
    }

    /// <summary>
    /// Equal lifetimes are still an error. The outcome is predictable, but the declaration is
    /// redundant and collapsing it silently is the failure mode this codebase avoids.
    /// </summary>
    [Fact]
    public void ADuplicatedConventionIsAmbiguousEvenWithEqualLifetimes() {
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
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0004");

        Assert.Contains("duplicated", diagnostic.GetMessage());
    }

    [Fact]
    public void AsSelfRegistersTheConcreteTypeRatherThanTheService() {
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
            """);

        Assert.Empty(assembly.Descriptors("IFoo"));
        Assert.Single(assembly.Descriptors("Foo"));
    }

    /// <summary>
    /// AsSelfWithInterfaces promises one instance reachable through the type and every interface,
    /// which is why it emits the cross-wire shape rather than two independent registrations.
    /// </summary>
    [Fact]
    public void AsSelfWithInterfacesSharesOneInstance() {
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
            """);

        var provider = assembly.BuildProvider();

        var asFoo = provider.GetService(assembly.Type("Foo"));
        var asIFoo = provider.GetService(assembly.Type("IFoo"));
        var asIBar = provider.GetService(assembly.Type("IBar"));

        Assert.NotNull(asFoo);
        Assert.Same(asFoo, asIFoo);
        Assert.Same(asFoo, asIBar);
    }

    /// <summary>
    /// The hole this closes: a concrete class implementing nothing, selected by namespace.
    /// </summary>
    [Fact]
    public void AConcreteTypeWithNoInterfaceRegistersByNamespace() {
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
            """);

        Assert.Single(assembly.Descriptors("OrderCalculator"));
        Assert.Single(assembly.Descriptors("OrderValidator"));
    }

    [Fact]
    public void NamespaceFiltersNarrowAnAssignabilityConvention() {
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
            """);

        // Filtered out entirely, which is DM0005 rather than silence.
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0005");
    }

    [Fact]
    public void NotInNamespacesExcludesAfterInclusions() {
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
            """);

        Assert.Empty(assembly.Descriptors("IFoo"));
    }

    [Fact]
    public void RegisterAllWithNoServiceTypeNeedsAShape() {
        var result = Run(
            """
            public class Thing { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().InNamespaceOf<Thing>().AsScoped();
                }
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Contains("AsSelf", diagnostic.GetMessage());
    }

    /// <summary>
    /// Without a filter it would match every class in the compilation, so it is refused.
    /// </summary>
    [Fact]
    public void RegisterAllWithNoServiceTypeAndNoFilterIsRefused() {
        var result = Run(
            """
            public class Thing { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().AsSelf().AsScoped();
                }
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Contains("every class", diagnostic.GetMessage());
    }

    [Fact]
    public void RegistersEveryTypeDeclaringTheServiceInterface() {
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
            """);

        var descriptors = assembly.Descriptors("IFoo");

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));
        Assert.Contains(descriptors, d => d.ImplementationType == assembly.Type("FooOne"));
        Assert.Contains(descriptors, d => d.ImplementationType == assembly.Type("FooTwo"));
    }

    [Fact]
    public void RegisteredServiceResolves() {
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
            """);

        Assert.IsType(assembly.Type("Foo"), assembly.ResolveRequired("IFoo"));
        Assert.Equal(ServiceLifetime.Scoped, assembly.Descriptor("IFoo").Lifetime);
    }

    /// <summary>
    /// An interface declaring that it extends another is a deliberate statement of substitutability,
    /// so a convention naming the base interface matches by declaration.
    /// </summary>
    [Fact]
    public void MatchesThroughInterfaceInheritance() {
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
            """);

        Assert.Equal(assembly.Type("Thing"), assembly.Descriptor("IFoo").ImplementationType);
    }

    /// <summary>
    /// Extending a class is a statement about implementation reuse rather than about the contract,
    /// and every subclass added later would join the convention with nobody revisiting it. So it
    /// takes an explicit opt-in.
    /// </summary>
    [Fact]
    public void DoesNotMatchThroughABaseClassByDefault() {
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
            """);

        // Nothing matched, which is DM0005 rather than silence.
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0005");
    }

    [Fact]
    public void MatchesThroughABaseClassWhenAskedTo() {
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
            """);

        Assert.Equal(assembly.Type("Thing"), assembly.Descriptor("IFoo").ImplementationType);
    }

    /// <summary>
    /// The make-or-break behaviour for open generics: the match registers the closed construction it
    /// actually implements, not the open definition.
    /// </summary>
    [Fact]
    public void ClosesAnOpenGenericAgainstEachImplementation() {
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
            """);

        var handler = assembly.Type("IHandler`2");

        var createOrder = handler.MakeGenericType(assembly.Type("CreateOrder"), assembly.Type("OrderId"));
        var rename = handler.MakeGenericType(assembly.Type("Rename"), assembly.Type("OrderId"));

        var provider = assembly.BuildProvider();

        Assert.IsType(assembly.Type("CreateOrderHandler"), provider.GetService(createOrder));
        Assert.IsType(assembly.Type("RenameHandler"), provider.GetService(rename));
    }

    /// <summary>
    /// A closed convention registers only that construction. Without this,
    /// RegisterAll&lt;IHandler&lt;A,B&gt;&gt;() would pick up every other closing too.
    /// </summary>
    [Fact]
    public void AClosedGenericConventionMatchesOnlyThatConstruction() {
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
            """);

        var repo = assembly.Type("IRepo`1");
        var provider = assembly.BuildProvider();

        Assert.IsType(
            assembly.Type("IntRepo"),
            provider.GetService(repo.MakeGenericType(typeof(int))));

        Assert.Null(provider.GetService(repo.MakeGenericType(typeof(string))));
    }

    [Fact]
    public void AnExplicitServiceAttributeBeatsTheConvention() {
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
            """);

        var descriptors = assembly.Descriptors("IFoo");

        // The attributed type is registered once, by its attribute, at the lifetime the attribute
        // declared — not a second time at the convention's lifetime.
        var attributed = descriptors.Where(d => d.ImplementationType == assembly.Type("Attributed")).ToArray();

        Assert.Single(attributed);
        Assert.Equal(ServiceLifetime.Singleton, attributed[0].Lifetime);

        var byConvention = descriptors.Where(d => d.ImplementationType == assembly.Type("ByConvention")).ToArray();

        Assert.Single(byConvention);
        Assert.Equal(ServiceLifetime.Transient, byConvention[0].Lifetime);
    }

    [Fact]
    public void OmittingTheLifetimeIsRefused() {
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
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Contains("lifetime", diagnostic.GetMessage());
    }

    /// <summary>
    /// The body is configuration read at compile time, so anything outside the closed set of calls
    /// has no meaning. Refused rather than skipped: dropping it would lose registrations while the
    /// build stayed green.
    /// </summary>
    [Theory]
    [InlineData("foreach (var t in new Type[0]) { conventions.RegisterAll<IFoo>().AsSingleton(); }")]
    [InlineData("if (DateTime.Now.Day > 1) { conventions.RegisterAll<IFoo>().AsSingleton(); }")]
    [InlineData("var x = 5;")]
    [InlineData("Helper(conventions);")]
    [InlineData("conventions.RegisterAll<IFoo>().AsSingleton().AsScoped();")]
    public void UnreadableStatementsAreRefused(string statement) {
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
              """);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0009");
    }

    // TwoConventionsMatchingOneTypeIsAmbiguous lived here. It asserted DM0004 for a type matched
    // through two different interfaces, which is now a legal two-role registration; see
    // EachRoleKeepsItsOwnLifetimeAndItsOwnInstance, which covers the same source and asserts what it
    // registers rather than that it refuses. TwoConventionsRegisteringOneServiceTypeIsAmbiguous
    // keeps DM0004 honest for the case it exists for.

    [Fact]
    public void AConcreteTypeWithNoAccessibleConstructorIsReported() {
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
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0006");

        Assert.Contains("Hidden", diagnostic.GetMessage());
    }

    /// <summary>
    /// The class carries no attribute saying it is registered, so this is the only thing at the
    /// declaration that explains why it is in the container.
    /// </summary>
    [Fact]
    public void ReportsWhatEachClassIsExposedAs() {
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
            """);

        var exposures = result.GeneratorDiagnostics.Where(d => d.Id == "DM0010").ToArray();

        Assert.Equal(2, exposures.Length);
        Assert.All(exposures, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));

        // The indirect match names the hop, which is what keeps it from reading as luck.
        Assert.Contains(exposures, d => d.GetMessage() == "Exposed as IFoo in TestModule");
        Assert.Contains(exposures, d => d.GetMessage() == "Exposed as IFoo in TestModule (via IFooPrime)");
    }

    /// <summary>
    /// The location is what makes DM0010 useful, and it is the one thing that cannot be stored
    /// directly in an incremental model — a Location pins its SyntaxTree — so it is rebuilt from
    /// primitives at output. This asserts the rebuild lands on the right line.
    /// </summary>
    [Fact]
    public void ExposureIsReportedOnTheClassItself() {
        const string body =
            """
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

        var exposure = Assert.Single(result.GeneratorDiagnostics.Where(d => d.Id == "DM0010"));
        var span = exposure.Location.GetLineSpan();

        Assert.EndsWith("Test.cs", span.Path);

        var lines = (Preamble + body).Replace("\r\n", "\n").Split('\n');

        Assert.Contains("class Foo", lines[span.StartLinePosition.Line]);
    }

    /// <summary>
    /// A type implementing IConventionModule that is not a module registers nothing at all, so it is
    /// reported rather than left to fail silently.
    /// </summary>
    [Fact]
    public void ConventionsOnANonModuleAreReported() {
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
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");

        Assert.Contains("NotAModule", diagnostic.GetMessage());
    }

    [Fact]
    public void EditingAnUnrelatedMethodBodyReusesTheCachedOutput() {
        const string template =
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Conventions;

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
            new Dictionary<string, string> { ["Test.cs"] = template.Replace("VALUE", "2") },
            withConventions: true);

        Assert.Equal(result.FirstRun, result.SecondRun);
        Assert.True(result.AllOutputsCached,
            "editing a method body cannot change any registration, so every output should be cached");
    }
}

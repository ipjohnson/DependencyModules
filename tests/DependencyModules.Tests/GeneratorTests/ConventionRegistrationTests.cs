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

    [Fact]
    public void TwoConventionsMatchingOneTypeIsAmbiguous() {
        var result = Run(
            """
            public interface IFoo { }
            public interface IBar { }

            public class Both : IFoo, IBar { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IFoo>().AsSingleton();
                    conventions.RegisterAll<IBar>().AsScoped();
                }
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0004");

        Assert.Contains("Both", diagnostic.GetMessage());
    }

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

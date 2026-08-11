using System.Linq;
using DependencyModules.Runtime.Conventions;
using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using GeneratorNames = DependencyModules.Conventions.ConventionContractSource;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator matches convention declarations by name, and the names live in two places.
/// </summary>
/// <remarks>
/// <para>
/// The contracts are declared in <c>DependencyModules.Runtime</c>; the generator that reads them is
/// an analyzer, and an analyzer must not load the runtime assembly. So it carries the namespace, the
/// interface name and the method name as string constants and matches on those.
/// </para>
/// <para>
/// Nothing but this test connects the two. Rename <c>IConventionModule</c>, or move it to another
/// namespace, and every convention in every project silently stops matching — a green build that
/// registers nothing, which is the failure mode this generator exists to prevent everywhere else.
/// </para>
/// </remarks>
public class ConventionContractTests {

    [Fact]
    public void TheGeneratorLooksForTheNamespaceTheContractsAreDeclaredIn() {
        Assert.Equal(GeneratorNames.Namespace, typeof(IConventionModule).Namespace);
    }

    [Fact]
    public void TheGeneratorLooksForTheInterfaceTheContractsDeclare() {
        Assert.Equal(GeneratorNames.ConventionModule, nameof(IConventionModule));
    }

    [Fact]
    public void TheGeneratorLooksForTheMethodTheInterfaceDeclares() {
        var method = Assert.Single(typeof(IConventionModule).GetMethods());

        Assert.Equal(GeneratorNames.ConventionMethod, method.Name);
    }

    /// <summary>
    /// The contracts are a compile-time DSL, so every verb has to be reachable from the chain.
    /// </summary>
    /// <remarks>
    /// A verb returning something other than <see cref="IConventionRegistration"/> would end the
    /// chain, which the fluent form exists to avoid. Asserted because it is the kind of thing a
    /// hurried addition gets wrong and nothing else would catch.
    /// </remarks>
    [Fact]
    public void EveryRegistrationVerbContinuesTheChain() {
        var breaks = typeof(IConventionRegistration).GetMethods()
            .Where(method => method.ReturnType != typeof(IConventionRegistration))
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(breaks);
    }

    /// <summary>
    /// Every entry point produces a registration to continue from.
    /// </summary>
    [Fact]
    public void EveryRegisterAllOverloadStartsTheChain() {
        var breaks = typeof(IConventionDefinitions).GetMethods()
            .Where(method => method.ReturnType != typeof(IConventionRegistration))
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(breaks);
    }

    private const string Preamble =
        """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;

        namespace TestNamespace;

        public interface IGreeter { string Greet(); }

        public class Greeter : IGreeter { public string Greet() => "hello"; }

        """;

    /// <summary>
    /// An ordinary public implementation registers, now that the interface is a public type in a
    /// referenced assembly.
    /// </summary>
    /// <remarks>
    /// This is the shape the design notes recorded as impossible: while the contracts were emitted
    /// into the consumer as <c>internal</c>, a public method taking one was CS0051, so explicit
    /// implementation was the only form that compiled and the interface name had to appear twice.
    /// The move to <c>DependencyModules.Runtime</c> is what retires that, and this is the assertion
    /// that it stays retired.
    /// </remarks>
    [Fact]
    public void AnImplicitPublicImplementationDeclaresConventions() {
        var assembly = GeneratedAssembly.Create(
            Preamble +
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSingleton();
                }
            }
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("hello", ((dynamic)provider.GetRequiredService(assembly.Type("IGreeter"))).Greet());
    }

    /// <summary>
    /// The explicit form still compiles and still registers, so nobody has to rewrite anything.
    /// </summary>
    [Fact]
    public void TheExplicitImplementationStillDeclaresConventions() {
        var assembly = GeneratedAssembly.Create(
            Preamble +
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSingleton();
                }
            }
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("hello", ((dynamic)provider.GetRequiredService(assembly.Type("IGreeter"))).Greet());
    }
}

using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// Scanning a referenced assembly with <c>InAssemblyOf&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// The library is compiled to real metadata and referenced, so the consuming compilation contains no
/// syntax tree for any of its types. That is the whole point: this path reads symbols out of
/// metadata, where the in-compilation path reads declarations.
/// </remarks>
public partial class ReferencedAssemblyScanTests
{
    private const string LibrarySource = """
        namespace ThePackage;

        public interface IHandler<TIn, TOut> { }

        public class CreateOrder { }
        public class RenameOrder { }
        public class OrderId { }

        public class CreateOrderHandler : IHandler<CreateOrder, OrderId> { }
        public class RenameOrderHandler : IHandler<RenameOrder, OrderId> { }

        // Invisible across an assembly boundary.
        internal class HiddenHandler : IHandler<CreateOrder, OrderId> { }

        // Visible, but the container could not construct it.
        public class UnconstructableHandler : IHandler<RenameOrder, OrderId> {
            private UnconstructableHandler() { }
        }

        public class Unrelated { }
        """;

    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;
        using ThePackage;

        namespace TestNamespace;

        """;

    private static (GeneratorResult Result, GeneratedAssembly? Assembly) Run(
        string module,
        bool compile = true
    )
    {
        var library = GeneratorTestHarness.CompileLibrary(LibrarySource, "ThePackage");
        var references = new[] { library.Reference };

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Preamble + module },
            additionalReferences: references
        );

        var assembly = compile
            ? GeneratedAssembly.Create(Preamble + module, additionalReferences: references)
            : null;

        return (result, assembly);
    }

    /// <summary>
    /// The capability: an open generic matched against a package's types, each registered against
    /// the closed construction it actually implements.
    /// </summary>
    [VerifyTest]
    public void RegistersTypesFromAReferencedAssembly()
    {
        var (result, assembly) = Run(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .AsScoped();
                }
            }
            """
        );

        result.AssertNoErrors();

        var handlerType = assembly!
            .Services.Select(d => d.ServiceType)
            .First(t => t.Name == "IHandler`2");

        var registered = assembly
            .Services.Where(d => d.ServiceType.Name == "IHandler`2")
            .Select(d => d.ImplementationType!.Name)
            .OrderBy(name => name)
            .ToArray();

        Verify(registered.SequenceEqual(["CreateOrderHandler", "RenameOrderHandler"]));
        Verify(handlerType is not null);
    }

    /// <summary>
    /// An internal type is not visible across the boundary, and an unconstructable one is refused
    /// the same way it would be in the compilation being built.
    /// </summary>
    [VerifyTest]
    public void SkipsWhatItCannotSeeOrConstruct()
    {
        var (result, assembly) = Run(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .AsScoped();
                }
            }
            """
        );

        var registered = assembly!
            .Services.Where(d => d.ServiceType.Name == "IHandler`2")
            .Select(d => d.ImplementationType!.Name)
            .ToArray();

        Verify(!registered.Contains("HiddenHandler"));
        Verify(!registered.Contains("UnconstructableHandler"));

        // Refused rather than dropped in silence.
        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0006"));
    }

    /// <summary>
    /// A registered service from the package resolves.
    /// </summary>
    [VerifyTest]
    public void TheRegistrationsResolve()
    {
        var (_, assembly) = Run(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .AsScoped();
                }
            }
            """
        );

        var descriptor = assembly!.Services.First(d =>
            d.ServiceType.Name == "IHandler`2" && d.ImplementationType!.Name == "CreateOrderHandler"
        );

        var provider = assembly.BuildProvider();

        Verify(provider.GetService(descriptor.ServiceType) is not null);
    }

    /// <summary>
    /// One source or the other. A convention naming an assembly must not pick up local types, and
    /// one that names none must not reach into the package.
    /// </summary>
    [VerifyTest]
    public void AConventionSeesOneSourceOnly()
    {
        var (_, assembly) = Run(
            """
            public class LocalHandler : IHandler<CreateOrder, OrderId> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .AsScoped();
                }
            }
            """
        );

        var registered = assembly!
            .Services.Where(d => d.ServiceType.Name == "IHandler`2")
            .Select(d => d.ImplementationType!.Name)
            .ToArray();

        Verify(!registered.Contains("LocalHandler"));
    }

    /// <summary>
    /// Absent the call, a convention scans the compilation being built — unchanged behaviour.
    /// </summary>
    [VerifyTest]
    public void WithoutTheCallOnlyLocalTypesMatch()
    {
        var (_, assembly) = Run(
            """
            public class LocalHandler : IHandler<CreateOrder, OrderId> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>)).AsScoped();
                }
            }
            """
        );

        var registered = assembly!
            .Services.Where(d => d.ServiceType.Name == "IHandler`2")
            .Select(d => d.ImplementationType!.Name)
            .ToArray();

        Verify(registered.SequenceEqual(["LocalHandler"]));
    }

    /// <summary>
    /// A match from a referenced assembly has no class to squiggle, so its diagnostics report at
    /// the convention that asked for it rather than nowhere.
    /// </summary>
    [VerifyTest]
    public void DiagnosticsForMetadataMatchesReportAtTheConvention()
    {
        var (result, _) = Run(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .AsScoped();
                }
            }
            """,
            compile: false
        );

        var exposures = result.GeneratorDiagnostics.Where(d => d.Id == "DM0010").ToArray();

        Verify(exposures.Length != 0);

        foreach (var diagnostic in exposures)
        {
            Verify(!diagnostic.Location.Equals(Location.None));
            Verify(diagnostic.Location.GetLineSpan().Path.Contains("Test.cs"));
        }
    }

    /// <summary>
    /// Filters apply to metadata types the same way they apply to local ones.
    /// </summary>
    [VerifyTest]
    public void FiltersApplyToMetadataTypes()
    {
        var (_, assembly) = Run(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>))
                        .InAssemblyOf<CreateOrder>()
                        .WithName("Create*")
                        .AsScoped();
                }
            }
            """
        );

        var registered = assembly!
            .Services.Where(d => d.ServiceType.Name == "IHandler`2")
            .Select(d => d.ImplementationType!.Name)
            .ToArray();

        Verify(registered.SequenceEqual(["CreateOrderHandler"]));
    }
}

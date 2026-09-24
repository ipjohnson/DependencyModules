using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// <c>Program.cs</c> names modules with assembly attributes and with calls to their static methods.
/// The generated <c>ApplicationModule</c> loads them. When the project declares
/// <c>ApplicationModule</c> in the root namespace, the declared module takes the place of the
/// generated one, so it has to load them too.
/// </summary>
public class DeclaredApplicationModuleTests
{
    private const string LibrarySource = """
        using DependencyModules.Runtime.Attributes;

        namespace Catalog;

        public interface ICatalog;

        [SingletonService(Realm = typeof(CatalogModule))]
        public class CatalogService : ICatalog;

        [DependencyModule(OnlyRealm = true)]
        public partial class CatalogModule;

        public interface IPricing;

        [SingletonService(Realm = typeof(PricingModule))]
        public class PricingService : IPricing;

        [DependencyModule(OnlyRealm = true)]
        public partial class PricingModule
        {
            public static void Warm() { }
        }
        """;

    private const string Program = """
        using Catalog;

        [assembly: CatalogModule]

        PricingModule.Warm();
        """;

    private const string DeclaredModule = """
        using DependencyModules.Runtime.Attributes;

        namespace WebShop;

        [DependencyModule]
        public partial class ApplicationModule;
        """;

    [Fact]
    public void TheGeneratedModule_LoadsTheModulesThatProgramNames()
    {
        var services = Apply(new Dictionary<string, string> { ["Program.cs"] = Program });

        Assert.True(Registered(services, "Catalog.ICatalog"));
        Assert.True(Registered(services, "Catalog.IPricing"));
    }

    [Fact]
    public void ADeclaredModule_LoadsTheModulesThatProgramNames()
    {
        var services = Apply(
            new Dictionary<string, string>
            {
                ["Program.cs"] = Program,
                ["ApplicationModule.cs"] = DeclaredModule,
            }
        );

        Assert.True(Registered(services, "Catalog.ICatalog"));
        Assert.True(Registered(services, "Catalog.IPricing"));
    }

    /// <summary>
    /// A module that the project declares itself. Its attribute is written by the same run of the
    /// generator.
    /// </summary>
    [Fact]
    public void ADeclaredModule_LoadsALocalModuleThatProgramNames()
    {
        var services = Apply(
            new Dictionary<string, string>
            {
                ["Program.cs"] = """
                using WebShop.Parts;

                [assembly: StockModule]

                System.Console.WriteLine();
                """,
                ["ApplicationModule.cs"] = DeclaredModule,
                ["Parts.cs"] = """
                using DependencyModules.Runtime.Attributes;

                namespace WebShop.Parts;

                public interface IStock;

                [SingletonService(Realm = typeof(StockModule))]
                public class Stock : IStock;

                [DependencyModule(OnlyRealm = true)]
                public partial class StockModule;
                """,
            }
        );

        Assert.True(Registered(services, "WebShop.Parts.IStock"));
    }

    private static int _counter;

    private static IServiceCollection Apply(Dictionary<string, string> sources)
    {
        var library = GeneratorTestHarness.CompileLibrary(
            LibrarySource,
            "DeclaredApplicationModuleCatalog",
            runGenerator: true
        );

        var assemblyName = "DeclaredApplicationModule" + Interlocked.Increment(ref _counter);

        var result = GeneratorTestHarness.Run(
            sources,
            new Dictionary<string, string> { ["RootNamespace"] = "WebShop" },
            OutputKind.ConsoleApplication,
            assemblyName,
            additionalReferences: [library.Reference]
        );

        result.AssertNoErrors();

        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);

        Assert.True(
            emitted.Success,
            string.Join(
                Environment.NewLine,
                emitted
                    .Diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error
                    )
                    .Select(diagnostic => $"  {diagnostic.Id} {diagnostic.GetMessage()}")
            )
        );

        var assembly = Assembly.Load(stream.ToArray());
        var module = (IDependencyModule)
            Activator.CreateInstance(assembly.GetType("WebShop.ApplicationModule")!)!;

        var services = new ServiceCollection();
        services.AddModules(module);

        return services;
    }

    private static bool Registered(IServiceCollection services, string serviceTypeName) =>
        services.Any(descriptor => descriptor.ServiceType.FullName == serviceTypeName);
}

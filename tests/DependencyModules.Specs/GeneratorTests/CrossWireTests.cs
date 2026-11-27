using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// A cross-wired class is registered once, and each interface it declares resolves that one
/// registration. These tests build the generated code and resolve from it, because the defects
/// were in code that did not compile or that threw when the module was applied.
/// </summary>
public class CrossWireTests
{
    private const string Preamble = """
        using System;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;

        namespace TestNamespace;

        public interface IReader { }

        public interface IWriter { }

        """;

    private static GeneratedAssembly Compile(string source) =>
        GeneratedAssembly.Create(Preamble + source);

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    [Fact]
    public void AKeyedClass_SharesOneInstanceUnderItsKey()
    {
        var assembly = Compile(
            """
            [CrossWireService(Key = "store")]
            public class Store : IReader, IWriter { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = assembly.BuildProvider();
        var store = provider.GetRequiredKeyedService(assembly.Type("Store"), "store");

        Assert.Same(store, provider.GetRequiredKeyedService(assembly.Type("IReader"), "store"));
        Assert.Same(store, provider.GetRequiredKeyedService(assembly.Type("IWriter"), "store"));
    }

    [Fact]
    public void AKeyedConventionWithAlsoAsSelf_SharesOneInstanceUnderItsKey()
    {
        var assembly = Compile(
            """
            public class Store : IReader { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IReader>().WithKey("rules").AlsoAsSelf().AsSingleton();
                }
            }
            """
        );

        var provider = assembly.BuildProvider();

        Assert.Same(
            provider.GetRequiredKeyedService(assembly.Type("Store"), "rules"),
            provider.GetRequiredKeyedService(assembly.Type("IReader"), "rules")
        );
    }

    [Theory]
    [InlineData("[CrossWireService(Using = RegistrationType.Try)]", "[DependencyModule]")]
    [InlineData("[CrossWireService(Using = RegistrationType.TryEnumerable)]", "[DependencyModule]")]
    [InlineData("[CrossWireService(Using = RegistrationType.Replace)]", "[DependencyModule]")]
    [InlineData("[CrossWireService]", "[DependencyModule(Using = RegistrationType.Try)]")]
    [InlineData("[CrossWireService]", "[DependencyModule(Using = RegistrationType.TryEnumerable)]")]
    [InlineData(
        "[CrossWireService]",
        "[DependencyModule(Using = RegistrationType.TryEnumerable, GenerateFactories = true)]"
    )]
    public void EachRegistrationType_SharesOneInstance(string attribute, string module)
    {
        var assembly = Compile(
            $$"""
            {{attribute}}
            public class Store : IReader, IWriter { }

            {{module}}
            public partial class TestModule;
            """
        );

        var provider = assembly.BuildProvider();
        var store = provider.GetRequiredService(assembly.Type("Store"));

        Assert.Same(store, provider.GetRequiredService(assembly.Type("IReader")));
        Assert.Same(store, provider.GetRequiredService(assembly.Type("IWriter")));
        Assert.Single(assembly.Descriptors("Store"));
    }

    [Fact]
    public void TheClassRegistration_UsesTheRegistrationTypeOfTheModule()
    {
        var result = Run(
            """
            [CrossWireService]
            public class Store : IReader { }

            [DependencyModule(Using = RegistrationType.Try)]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var registrations = result.SourceContaining("Dependencies");

        Assert.Contains("services.TryAddSingleton(", registrations);
        Assert.Contains("services.TryAdd(new", registrations);
        Assert.DoesNotContain("services.Add(new", registrations);
    }

    /// <summary>
    /// TryAddEnumerable identifies a factory registration by the return type of its delegate.
    /// </summary>
    [Fact]
    public void TryEnumerable_KeepsOneRegistrationOfEachInterface()
    {
        var assembly = Compile(
            """
            [CrossWireService(Using = RegistrationType.TryEnumerable)]
            public class Store : IReader { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var module = (DependencyModules.Runtime.Interfaces.IDependencyModule)
            Activator.CreateInstance(assembly.Type("TestModule"))!;

        module.InternalApplyServices(assembly.Services);

        Assert.Single(assembly.Descriptors("IReader"));
        Assert.Single(assembly.Descriptors("Store"));
    }

    [Fact]
    public void AGeneratedFactoryWithTryEnumerable_CanBeApplied()
    {
        var assembly = Compile(
            """
            [SingletonService]
            public class Reader : IReader { }

            [DependencyModule(Using = RegistrationType.TryEnumerable, GenerateFactories = true)]
            public partial class TestModule;
            """
        );

        Assert.Equal("Reader", assembly.ResolveRequired("IReader").GetType().Name);
    }

    [Theory]
    [InlineData("AlsoAsSelf()")]
    [InlineData("AsSelfWithInterfaces()")]
    public void AConventionThatCrossWiresAGenericClass_IsReported(string shape)
    {
        var result = Run(
            $$"""
            public interface IRepository<T> { }

            public interface IAudit<T> { }

            public class Repository<T> : IRepository<T>, IAudit<T> { }

            public class Plain : IRepository<int> { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll(typeof(IRepository<>)).{{shape}}.AsScoped();
                }
            }
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0014");
        Assert.Contains("'Repository'", diagnostic.GetMessage());
        Assert.Contains("the convention registering", diagnostic.GetMessage());
        Assert.DoesNotContain("Repository<>", result.SourceContaining("ConventionDependencies"));
        Assert.Contains("Plain", result.SourceContaining("ConventionDependencies"));
    }

    [Fact]
    public void AClassThatInheritsItsInterfaces_IsRegistered_AndReported()
    {
        var result = Run(
            """
            public class ReaderBase : IReader { }

            [CrossWireService]
            public class Store : ReaderBase { }

            [DependencyModule(RegisterJsonSerializers = true)]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0024");
        Assert.Contains("'Store'", diagnostic.GetMessage());

        var registrations = result.SourceContaining("Dependencies");

        Assert.Contains("typeof(global::TestNamespace.Store)", registrations);
        Assert.DoesNotContain("IReader", registrations);
    }

    [Fact]
    public void AClassWithNoInterface_IsRegistered_WithoutAWarning()
    {
        var assembly = Compile(
            """
            [CrossWireService]
            public class Store { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Assert.Equal("Store", assembly.ResolveRequired("Store").GetType().Name);
        Assert.DoesNotContain(
            Run(
                """
                [CrossWireService]
                public class Store { }

                [DependencyModule]
                public partial class TestModule;
                """
            ).GeneratorDiagnostics,
            d => d.Id == "DM0024"
        );
    }

    [Fact]
    public void AnInterfaceOnAnotherPartialDeclaration_IsCrossWired()
    {
        var assembly = Compile(
            """
            [CrossWireService]
            public partial class Store { }

            public partial class Store : IReader { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = assembly.BuildProvider();

        Assert.Same(
            provider.GetRequiredService(assembly.Type("Store")),
            provider.GetRequiredService(assembly.Type("IReader"))
        );
    }

    [Fact]
    public void AFactoryMethod_RegistersItsReturnTypeAndTheInterfacesItDeclares()
    {
        var assembly = Compile(
            """
            public class Store : IReader, IWriter
            {
                public static int Created;

                public Store() => Created++;
            }

            public static class StoreFactories
            {
                [CrossWireService]
                public static Store CreateStore() => new Store();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var provider = assembly.BuildProvider();
        var store = provider.GetRequiredService(assembly.Type("Store"));

        Assert.Same(store, provider.GetRequiredService(assembly.Type("IReader")));
        Assert.Same(store, provider.GetRequiredService(assembly.Type("IWriter")));
        Assert.Equal(1, assembly.Type("Store").GetField("Created")!.GetValue(null));
    }

    [Fact]
    public void AFactoryMethodThatReturnsAnInterface_RegistersIt()
    {
        var assembly = Compile(
            """
            public class Reader : IReader { }

            public static class ReaderFactories
            {
                [CrossWireService]
                public static IReader CreateReader() => new Reader();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Assert.Equal("Reader", assembly.ResolveRequired("IReader").GetType().Name);
    }
}

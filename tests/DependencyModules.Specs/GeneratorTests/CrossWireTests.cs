using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// A cross-wired class is registered once, and each interface it declares resolves that one
/// registration. These tests build the generated code and resolve from it, because the defects
/// were in code that did not compile or that threw when the module was applied.
/// </summary>
public partial class CrossWireTests
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

    [VerifyTest]
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

        Verify(
            ReferenceEquals(
                store,
                provider.GetRequiredKeyedService(assembly.Type("IReader"), "store")
            )
        );
        Verify(
            ReferenceEquals(
                store,
                provider.GetRequiredKeyedService(assembly.Type("IWriter"), "store")
            )
        );
    }

    [VerifyTest]
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

        Verify(
            ReferenceEquals(
                provider.GetRequiredKeyedService(assembly.Type("Store"), "rules"),
                provider.GetRequiredKeyedService(assembly.Type("IReader"), "rules")
            )
        );
    }

    [VerifyTest]
    [Row("[CrossWireService(Using = RegistrationType.Try)]", "[DependencyModule]")]
    [Row("[CrossWireService(Using = RegistrationType.TryEnumerable)]", "[DependencyModule]")]
    [Row("[CrossWireService(Using = RegistrationType.Replace)]", "[DependencyModule]")]
    [Row("[CrossWireService]", "[DependencyModule(Using = RegistrationType.Try)]")]
    [Row("[CrossWireService]", "[DependencyModule(Using = RegistrationType.TryEnumerable)]")]
    [Row(
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

        Verify(ReferenceEquals(store, provider.GetRequiredService(assembly.Type("IReader"))));
        Verify(ReferenceEquals(store, provider.GetRequiredService(assembly.Type("IWriter"))));
        Single(assembly.Descriptors("Store"));
    }

    [VerifyTest]
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

        Verify(registrations.Contains("services.TryAddSingleton("));
        Verify(registrations.Contains("services.TryAdd(new"));
        Verify(!registrations.Contains("services.Add(new"));
    }

    /// <summary>
    /// TryAddEnumerable identifies a factory registration by the return type of its delegate.
    /// </summary>
    [VerifyTest]
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

        Single(assembly.Descriptors("IReader"));
        Single(assembly.Descriptors("Store"));
    }

    [VerifyTest]
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

        Verify(assembly.ResolveRequired("IReader").GetType().Name == "Reader");
    }

    [VerifyTest]
    [Row("AlsoAsSelf()")]
    [Row("AsSelfWithInterfaces()")]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0014");
        Verify(diagnostic.GetMessage().Contains("'Repository'"));
        Verify(diagnostic.GetMessage().Contains("the convention registering"));
        Verify(!result.SourceContaining("ConventionDependencies").Contains("Repository<>"));
        Verify(result.SourceContaining("ConventionDependencies").Contains("Plain"));
    }

    [VerifyTest]
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

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0024");
        Verify(diagnostic.GetMessage().Contains("'Store'"));

        var registrations = result.SourceContaining("Dependencies");

        Verify(registrations.Contains("typeof(global::TestNamespace.Store)"));
        Verify(!registrations.Contains("IReader"));
    }

    [VerifyTest]
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

        Verify(assembly.ResolveRequired("Store").GetType().Name == "Store");

        var diagnostics = Run(
            """
            [CrossWireService]
            public class Store { }

            [DependencyModule]
            public partial class TestModule;
            """
        ).GeneratorDiagnostics;

        Verify(!diagnostics.Any(d => d.Id == "DM0024"));
    }

    [VerifyTest]
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

        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("Store")),
                provider.GetRequiredService(assembly.Type("IReader"))
            )
        );
    }

    [VerifyTest]
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

        Verify(ReferenceEquals(store, provider.GetRequiredService(assembly.Type("IReader"))));
        Verify(ReferenceEquals(store, provider.GetRequiredService(assembly.Type("IWriter"))));
        Verify(Equals(assembly.Type("Store").GetField("Created")!.GetValue(null), 1));
    }

    [VerifyTest]
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

        Verify(assembly.ResolveRequired("IReader").GetType().Name == "Reader");
    }
}

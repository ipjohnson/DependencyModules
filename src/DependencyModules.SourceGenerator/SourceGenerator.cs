using System.Collections.Immutable;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// The generator this package ships. It owns <c>[DependencyModule]</c>, which is why it writes the
/// module partial and a generator built on the same base class does not.
/// </summary>
[Generator]
public class SourceGenerator : BaseSourceGenerator {

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new ServiceSourceGenerator();
        yield return new InterceptorSourceGenerator();
        yield return new global::DependencyModules.Conventions.ConventionGenerator();
    }

    /// <summary>
    /// Writes the module for <c>[DependencyModule]</c>. The base class declines that attribute by
    /// default, so that a third party building on it contributes to these modules rather than
    /// declaring every one of them a second time; this is the generator that claim belongs to.
    /// </summary>
    protected override void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) {

        DependencyModuleWriter.Register(context, valuesProvider, generateAttribute: true);

        // DM0016. Registered here rather than on the base class so that a framework generator loaded
        // alongside this one does not report the same usage twice.
        context.RegisterSourceOutput(
            valuesProvider.Combine(AssemblyModuleAttributeDiagnostics.Collect(context))
                .Combine(context.CompilationProvider),
            AssemblyModuleAttributeDiagnostics.Report);
    }
}
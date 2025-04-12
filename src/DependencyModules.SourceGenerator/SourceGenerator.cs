using System.Collections.Immutable;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator;

[Generator]
public class SourceGenerator : BaseSourceGenerator {

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new ServiceSourceGenerator();
    }

    protected override void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) {
     
        var moduleWriter = new DependencyModuleWriter(true);
        
        context.RegisterSourceOutput(
            valuesProvider,
            moduleWriter.GenerateSource
        );
    }
}
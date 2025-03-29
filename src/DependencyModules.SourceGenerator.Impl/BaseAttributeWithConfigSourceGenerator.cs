using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyModules.SourceGenerator.Impl;

public abstract class BaseAttributeWithConfigSourceGenerator<TModel, TConfig> : IDependencyModuleSourceGenerator {

    public void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(AttributeTypes().ToArray());

        var serviceModelProvider = context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateAttributeModel
        ).WithComparer(GetComparer());

        var collection =
            serviceModelProvider.Collect();

        var config =
            context.AnalyzerConfigOptionsProvider.Select(GenerateConfigAttributeModel).WithComparer(GetConfigComparer());

        var valuesProvider = incrementalValueProvider.Combine(config);

        context.RegisterSourceOutput(
            valuesProvider.Combine(collection),
            GenerateSourceOutput
        );
    }

    protected abstract IEnumerable<ITypeDefinition> AttributeTypes();

    protected abstract void GenerateSourceOutput(SourceProductionContext arg1,
        (((ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right) Left, TConfig Right) Left, ImmutableArray<TModel> Right) arg2);

    protected abstract TConfig GenerateConfigAttributeModel(AnalyzerConfigOptionsProvider arg1, CancellationToken arg2);

    protected abstract IEqualityComparer<TModel> GetComparer();

    protected abstract TModel GenerateAttributeModel(GeneratorSyntaxContext arg1, CancellationToken arg2);

    protected abstract IEqualityComparer<TConfig> GetConfigComparer();
}
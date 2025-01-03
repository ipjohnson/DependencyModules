using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl;

public interface ISourceGenerator {
    void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<ModuleEntryPointModel> incrementalValueProvider);
}

public abstract class BaseAttributeSourceGenerator<T> : ISourceGenerator {
    protected abstract IEnumerable<ITypeDefinition> AttributeTypes();

    public void SetupGenerator(IncrementalGeneratorInitializationContext context, 
        IncrementalValuesProvider<ModuleEntryPointModel> incrementalValueProvider) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(AttributeTypes().ToArray());

        var serviceModelProvider = context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateAttributeModel
        ).WithComparer(GetComparer());

        var collection =
            serviceModelProvider.Collect();

        context.RegisterSourceOutput(
            incrementalValueProvider.Combine(collection),
            GenerateSourceOutput
        );
    }

    protected abstract void GenerateSourceOutput(SourceProductionContext arg1, (ModuleEntryPointModel Left, ImmutableArray<T> Right) arg2);

    protected abstract IEqualityComparer<T> GetComparer();

    protected abstract T GenerateAttributeModel(GeneratorSyntaxContext arg1, CancellationToken arg2);
}
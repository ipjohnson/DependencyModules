using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl;

public interface ISourceGenerator {
    void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider);
}

public abstract class BaseAttributeSourceGenerator<T> : ISourceGenerator {

    public void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {
        var classSelector = new SyntaxSelector<MemberDeclarationSyntax>(AttributeTypes().ToArray());

        var serviceModelProvider = context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateAttributeModel
        ).WithComparer(GetComparer());

        var collection =
            serviceModelProvider.Collect();

        context.RegisterSourceOutput(
            incrementalValueProvider.Collect().Combine(collection),
            WrapGenerateSourceOutput
        );
    }

    private void WrapGenerateSourceOutput(SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) data) {
        var config = data.Left.FirstOrDefault().Right;

        if (config != null) {
            FileLogger.Wrap(
                LoggerName, config,logger => GenerateSourceOutput(context, data,logger));
        }
    }

    protected virtual string LoggerName => GetType().Name;

    protected abstract IEnumerable<ITypeDefinition> AttributeTypes();

    protected abstract void GenerateSourceOutput(SourceProductionContext arg1,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) valueTuple,
        FileLogger logger);

    protected abstract IEqualityComparer<T> GetComparer();

    protected abstract T GenerateAttributeModel(GeneratorSyntaxContext arg1, CancellationToken arg2);
}
using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl;

public interface IDependencyModuleSourceGenerator {
    void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider);
}

public abstract class BaseAttributeSourceGenerator<T> : IDependencyModuleSourceGenerator {

    public void SetupGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {

        var attributeTypes = AttributeTypes().ToArray();

        if (attributeTypes.Length == 0) {
            return;
        }

        context.RegisterSourceOutput(
            incrementalValueProvider.Collect().Combine(CollectModels(context, attributeTypes)),
            WrapGenerateSourceOutput
        );
    }

    /// <summary>
    /// One provider per attribute, merged. See <see cref="AttributeModelCollector"/>.
    /// </summary>
    private IncrementalValueProvider<ImmutableArray<T>> CollectModels(
        IncrementalGeneratorInitializationContext context, ITypeDefinition[] attributeTypes) =>
        AttributeModelCollector.Collect(
            context, attributeTypes, GenerateAttributeModel, GetComparer(), IgnoredModel);

    private void WrapGenerateSourceOutput(SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) data) {
        var config = data.Left.FirstOrDefault().Right;

        if (config != null) {
            FileLogger.Wrap(
                LoggerName,
                config,
                logger => GenerateSourceOutput(context, data, logger),
                // Surfaced as a build error rather than discarded. A generator that fails quietly
                // produces a green build with no registrations, which is far harder to diagnose
                // than a failed one.
                exception => context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.GeneratorFailure,
                        Location.None,
                        $"{exception.GetType().Name}: {exception.Message}")));
        }
    }

    protected virtual string LoggerName => GetType().Name;

    protected abstract IEnumerable<ITypeDefinition> AttributeTypes();

    protected abstract void GenerateSourceOutput(SourceProductionContext arg1,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) valueTuple,
        FileLogger logger);

    protected abstract IEqualityComparer<T> GetComparer();

    protected abstract T GenerateAttributeModel(GeneratorAttributeSyntaxContext arg1, CancellationToken arg2);

    /// <summary>
    /// The sentinel this generator emits for a declaration it does not own. Every model type already
    /// has one, and the writers already skip it.
    /// </summary>
    protected abstract T IgnoredModel { get; }
}
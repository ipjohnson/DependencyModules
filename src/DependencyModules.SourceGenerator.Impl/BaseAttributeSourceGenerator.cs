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

        // One provider, two outputs. Sharing it means the transform runs once; registering
        // CollectModels twice would do the same discovery work twice over.
        var models = incrementalValueProvider.Collect().Combine(CollectModels(context, attributeTypes));

        context.RegisterSourceOutput(models, WrapGenerateSourceOutput);

        // Diagnostics separately, with the compilation combined in. A diagnostic needs the syntax
        // tree its location came from before Roslyn will let .editorconfig or #pragma silence it,
        // and only the compilation can turn a file path back into a tree. Combining the compilation
        // into the emitting output instead would re-emit every file on every keystroke, since the
        // compilation changes with each one; this output emits nothing, so re-running it is a walk
        // over models that are already cached.
        context.RegisterSourceOutput(models.Combine(context.CompilationProvider), WrapReportDiagnostics);
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

    private void WrapReportDiagnostics(SourceProductionContext context,
        ((ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) Left,
            Compilation Right) data) {
        var config = data.Left.Left.FirstOrDefault().Right;

        if (config != null) {
            FileLogger.Wrap(
                LoggerName,
                config,
                logger => ReportDiagnostics(context, data.Left, new SyntaxTreeLookup(data.Right), logger),
                exception => context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.GeneratorFailure,
                        Location.None,
                        $"{exception.GetType().Name}: {exception.Message}")));
        }
    }

    /// <summary>
    /// Reports this generator's diagnostics. Emits nothing.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GenerateSourceOutput"/> so that the diagnostics can see the
    /// compilation and the emission does not have to. A generator with nothing to report leaves it
    /// alone; the conditions belong beside the ones emission uses to skip a model, not duplicated.
    /// </remarks>
    protected virtual void ReportDiagnostics(SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<T> Right) data,
        SyntaxTreeLookup lookup,
        FileLogger logger) { }

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
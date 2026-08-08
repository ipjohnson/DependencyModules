using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// Generates the wrapper types that route intercepted services through their interceptors, and
/// registers each wrapper as a decorator of the service.
/// </summary>
/// <remarks>
/// Registration reuses the decorator path unchanged: a wrapper is just a decorator whose body was
/// generated rather than written. Everything that path already handles — lifetime preservation, the
/// three descriptor shapes, global ordering — applies without change.
/// </remarks>
public class InterceptorSourceGenerator : BaseAttributeSourceGenerator<InterceptorModel> {
    private readonly IEqualityComparer<InterceptorModel> _comparer = new InterceptorModelComparer();

    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.InterceptAttribute
    };

    protected override string LoggerName => "InterceptorSourceGenerator";

    protected override IEnumerable<ITypeDefinition> AttributeTypes() {
        return _attributeTypes;
    }

    protected override InterceptorModel IgnoredModel => InterceptorModel.Ignore;

    protected override IEqualityComparer<InterceptorModel> GetComparer() {
        return _comparer;
    }

    protected override InterceptorModel GenerateAttributeModel(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken) {
        // A refusal travels on the model so the output stage, which owns the diagnostic context, can
        // report it. Reporting from the transform is not possible.
        return InterceptorModelUtility.GetInterceptorModel(context, cancellationToken);
    }

    protected override void GenerateSourceOutput(
        SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<InterceptorModel> Right) inputData,
        FileLogger logger) {

        if (inputData.Left.Length == 0 || inputData.Right.Length == 0) {
            return;
        }

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(inputData.Left);

        var usable = ReportUnsupported(context, inputData.Right, logger);

        if (usable.Count == 0) {
            return;
        }

        var writer = new InterceptorFileWriter();

        foreach (var model in usable) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var wrapperName = $"{model.ImplementationType.Name.Replace(".", "_")}_Intercepted";

            logger.Info($"Generating '{wrapperName}' for '{model.ServiceType}' with {model.Members.Count} member(s).");

            context.AddSource(
                $"{wrapperName}.g.cs",
                writer.Write(model, wrapperName, model.ImplementationType.Namespace));
        }

        // One registration file per module, so every wrapper is applied wherever its service is.
        foreach (var entryPointModel in entryPointList) {
            var registrationWriter = new InterceptorRegistrationWriter();

            context.AddSource(
                EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel)
                    .EntryPointType.GetFileNameHint(configurationModel.RootNamespace, "Interceptors"),
                registrationWriter.Write(
                    EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel),
                    configurationModel,
                    usable));
        }
    }

    /// <summary>
    /// Reports declarations that cannot be intercepted and drops them, so an unsupported shape
    /// produces an explanation rather than a wrapper that does not compile.
    /// </summary>
    private static IReadOnlyList<InterceptorModel> ReportUnsupported(
        SourceProductionContext context, ImmutableArray<InterceptorModel> models, FileLogger logger) {

        var usable = new List<InterceptorModel>();

        foreach (var model in models) {
            if (model.Refusal != null) {
                logger.Error($"Cannot intercept: {model.Refusal.Message}");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.CannotIntercept,
                        Location.None,
                        model.Refusal.Message));

                continue;
            }

            if (model.IsIgnored || model.Members.Count == 0) {
                continue;
            }

            usable.Add(model);
        }

        return usable;
    }
}

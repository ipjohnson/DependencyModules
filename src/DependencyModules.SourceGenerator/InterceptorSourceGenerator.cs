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

    protected override IEqualityComparer<InterceptorModel> GetComparer() {
        return _comparer;
    }

    protected override InterceptorModel GenerateAttributeModel(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var model = InterceptorModelUtility.GetInterceptorModel(context, cancellationToken, out var unsupported);

        if (model != null) {
            return model;
        }

        // The reason travels on the sentinel so the output stage, which owns the diagnostic context,
        // can report it. Reporting from the transform is not possible.
        return unsupported == null
            ? InterceptorModel.Ignore
            : InterceptorModel.Ignore with { ServiceType = TypeDefinition.Get("", unsupported) };
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

            logger.Info($"Generating '{wrapperName}' for '{model.ServiceType.Name}' with {model.Members.Count} member(s).");

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
            if (!model.IsIgnored && model.Members.Count > 0) {
                usable.Add(model);
                continue;
            }

            var reason = model.ServiceType.Name;

            if (model.IsIgnored || string.IsNullOrEmpty(reason) || reason == "Ignore") {
                continue;
            }

            logger.Error($"Cannot intercept: {reason}");

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.CannotIntercept,
                    Location.None,
                    reason));
        }

        return usable;
    }
}

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

        // Filtering only; ReportUnsupported's explanations live in ReportDiagnostics, which shares
        // the same predicate.
        var usable = Usable(inputData.Right);

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

        // One registration file per module, carrying the interceptions that belong to that module.
        // This used to hand every module the whole compilation's worth: an OnlyRealm module emitted
        // applicators for interceptions that named no realm and had nothing to do with it, which
        // either wrapped an unrelated service or — when the leaked interceptor needed a dependency
        // the isolated container did not have — threw while building the provider.
        foreach (var entryPointModel in EntryModelUtil.RegistrationTargets(entryPointList)) {
            var registrationWriter = new InterceptorRegistrationWriter();

            context.AddSource(
                EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel)
                    .EntryPointType.GetFileNameHint(configurationModel.RootNamespace, "Interceptors"),
                registrationWriter.Write(
                    EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel),
                    configurationModel,
                    ForModule(usable, entryPointModel)));
        }
    }

    /// <summary>
    /// The interceptions one module is responsible for applying.
    /// </summary>
    /// <remarks>
    /// The same rule services and decorators already follow: a realm-scoped interception belongs
    /// only to the module it names, and an unscoped one belongs to every module that is not
    /// realm-only.
    /// </remarks>
    private static IReadOnlyList<InterceptorModel> ForModule(
        IReadOnlyList<InterceptorModel> models, ModuleEntryPointModel entryPointModel) {

        var onlyRealm = entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.OnlyRealm);
        var selected = new List<InterceptorModel>();

        foreach (var model in models) {
            if (model.Realm != null) {
                if (model.Realm.Equals(entryPointModel.EntryPointType)) {
                    selected.Add(model);
                }

                continue;
            }

            if (!onlyRealm) {
                selected.Add(model);
            }
        }

        return selected;
    }

    /// <summary>
    /// The interceptions a wrapper is worth generating for.
    /// </summary>
    /// <remarks>
    /// Drops a declaration that cannot be intercepted at all, and one where no member has an
    /// interceptor able to serve it — a wrapper for the second would forward every call untouched.
    /// Both are explained by <see cref="ReportDiagnostics"/>; this only decides what to write.
    /// </remarks>
    private static IReadOnlyList<InterceptorModel> Usable(ImmutableArray<InterceptorModel> models) {
        var usable = new List<InterceptorModel>();

        foreach (var model in models) {
            if (model.Refusal != null || model.IsIgnored || model.Members.Count == 0) {
                continue;
            }

            if (!ServesAnyMember(model)) {
                continue;
            }

            usable.Add(model);
        }

        return usable;
    }

    /// <summary>Whether any member has an interceptor that can serve it.</summary>
    private static bool ServesAnyMember(InterceptorModel model) =>
        model.Members.Any(member =>
            model.Interceptors.Any(interceptor => interceptor.CanServe(member.Kind)));

    /// <summary>
    /// Why an interception was refused, or is quietly absent from members it was applied to.
    /// </summary>
    /// <remarks>
    /// Reported apart from emission so the locations carry their syntax tree, which is what lets
    /// one of these be silenced where it is written rather than only across the whole project.
    /// </remarks>
    protected override void ReportDiagnostics(SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<InterceptorModel> Right) data,
        SyntaxTreeLookup lookup,
        FileLogger logger) {

        if (data.Left.Length == 0 || data.Right.Length == 0) {
            return;
        }

        foreach (var model in data.Right) {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (model.Refusal != null) {
                logger.Error($"Cannot intercept: {model.Refusal.Message}");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.CannotIntercept,
                        model.Location?.ToLocationOrNone(lookup) ?? Location.None,
                        model.Refusal.Message));

                continue;
            }

            if (model.IsIgnored || model.Members.Count == 0) {
                continue;
            }

            // Reported whether or not the model survives Usable: an interception that serves no
            // member at all is exactly the case worth explaining, and dropping it first is what
            // used to make it invisible.
            ReportUnservedMembers(context, model, lookup, logger);
        }
    }

    /// <summary>
    /// Reports interceptors that are quietly absent from some of the members they were applied to.
    /// </summary>
    /// <remarks>
    /// The generator picks per member from the three interceptor interfaces, and an interceptor
    /// implementing none of the one a member needs was simply left out of that member's chain. That
    /// is an interceptor that does not run, which is a correctness question rather than a style one:
    /// an argument-rewriting interceptor stops rewriting, and an authorisation gate stops gating.
    ///
    /// One diagnostic per interceptor and member shape, so a wide interface produces one line rather
    /// than one per member.
    /// </remarks>
    private static void ReportUnservedMembers(
        SourceProductionContext context, InterceptorModel model, SyntaxTreeLookup lookup,
        FileLogger logger) {

        foreach (var interceptor in model.Interceptors) {
            foreach (var kind in new[] {
                         InterceptorKind.Sync, InterceptorKind.Async, InterceptorKind.Stream
                     }) {

                if (interceptor.CanServe(kind)) {
                    continue;
                }

                var unserved = model.Members
                    .Where(member => member.Kind == kind)
                    .Select(member => member.Name)
                    .Distinct()
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                if (unserved.Length == 0) {
                    continue;
                }

                logger.Error(
                    $"'{interceptor.Type.Name}' does not implement {InterfaceFor(kind)}, so it is not " +
                    $"applied to {string.Join(", ", unserved)} on '{model.ServiceType.Name}'.");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.InterceptorCannotServeMembers,
                        model.Location?.ToLocationOrNone(lookup) ?? Location.None,
                        interceptor.Type.Name,
                        InterfaceFor(kind),
                        DescriptionFor(kind),
                        model.ServiceType.Name,
                        string.Join(", ", unserved)));
            }
        }
    }

    private static string InterfaceFor(InterceptorKind kind) =>
        kind switch {
            InterceptorKind.Async => "IAsyncInterceptor",
            InterceptorKind.Stream => "IAsyncEnumerableInterceptor",
            _ => "IInterceptor"
        };

    private static string DescriptionFor(InterceptorKind kind) =>
        kind switch {
            InterceptorKind.Async => "the members returning a task",
            InterceptorKind.Stream => "the members returning an async stream",
            _ => "the members returning a value directly"
        };
}

using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// Emits the decorator registrations declared by <c>[Decorator]</c> on a class and <c>[Decorate]</c>
/// on a module.
/// </summary>
/// <remarks>
/// The decoration itself lives in <c>DecoratorHelper</c> at run time, so the generated code is a
/// pair of type references. Emitting the descriptor rewrite would mean reproducing its awkward parts
/// — capturing the descriptor before replacing the slot, closing an open generic decorator, covering
/// all three descriptor shapes — at every call site.
/// </remarks>
public class DecoratorSourceGenerator : BaseAttributeSourceGenerator<DecoratorModel> {
    private readonly IEqualityComparer<DecoratorModel> _comparer = new DecoratorModelComparer();

    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.DecoratorAttribute
    };

    protected override string LoggerName => "DecoratorSourceGenerator";

    protected override IEnumerable<ITypeDefinition> AttributeTypes() {
        return _attributeTypes;
    }

    protected override DecoratorModel IgnoredModel => DecoratorModel.Ignore;

    protected override IEqualityComparer<DecoratorModel> GetComparer() {
        return _comparer;
    }

    protected override DecoratorModel GenerateAttributeModel(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        return DecoratorModelUtility.GetDecoratorModel(context, cancellationToken) ?? DecoratorModel.Ignore;
    }

    protected override void GenerateSourceOutput(
        SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<DecoratorModel> Right) inputData,
        FileLogger logger) {

        if (inputData.Left.Length == 0) {
            return;
        }

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(inputData.Left);

        foreach (var entryPointModel in entryPointList) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var decorators = CollectDecorators(context, entryPointModel, inputData.Right, logger);

            if (decorators.Count == 0) {
                continue;
            }

            var writer = new DecoratorFileWriter();
            var output = writer.Write(
                EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel),
                configurationModel,
                decorators);

            context.AddSource(
                EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel)
                    .EntryPointType.GetFileNameHint(configurationModel.RootNamespace, "Decorators"),
                output);
        }
    }

    /// <summary>
    /// The decorators that belong to one module: those declared on a class, filtered by realm, plus
    /// those the module declares itself with <c>[Decorate]</c>.
    /// </summary>
    private static IReadOnlyList<DecoratorModel> CollectDecorators(
        SourceProductionContext context,
        ModuleEntryPointModel entryPointModel,
        ImmutableArray<DecoratorModel> declared,
        FileLogger logger) {

        var decorators = new List<DecoratorModel>();

        foreach (var decorator in declared) {
            if (decorator.IsIgnored) {
                continue;
            }

            // A realm-scoped decorator belongs only to its realm. An unscoped one belongs to every
            // module that is not realm-only, matching how service registrations behave.
            if (decorator.Realm != null) {
                if (decorator.Realm.Equals(entryPointModel.EntryPointType)) {
                    decorators.Add(decorator);
                }

                continue;
            }

            if (!entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.OnlyRealm)) {
                decorators.Add(decorator);
            }
        }

        decorators.AddRange(DecoratorModelUtility.GetModuleDeclaredDecorators(entryPointModel));

        ReportAmbiguousOrdering(context, decorators, logger);

        return decorators;
    }

    /// <summary>
    /// Two decorators of one service sharing an order nest in an order nobody declared, so it is
    /// reported rather than resolved arbitrarily.
    /// </summary>
    private static void ReportAmbiguousOrdering(
        SourceProductionContext context, IReadOnlyList<DecoratorModel> decorators, FileLogger logger) {

        for (var i = 0; i < decorators.Count; i++) {
            for (var j = i + 1; j < decorators.Count; j++) {
                if (decorators[i].Order != decorators[j].Order ||
                    !decorators[i].ServiceType.Equals(decorators[j].ServiceType)) {
                    continue;
                }

                logger.Error(
                    $"'{decorators[i].DecoratorType.Name}' and '{decorators[j].DecoratorType.Name}' both " +
                    $"decorate '{decorators[i].ServiceType.Name}' with order {decorators[i].Order}.");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.AmbiguousDecoratorOrder,
                        Location.None,
                        decorators[i].DecoratorType.Name,
                        decorators[j].DecoratorType.Name,
                        decorators[i].ServiceType.Name,
                        decorators[i].Order));
            }
        }
    }
}

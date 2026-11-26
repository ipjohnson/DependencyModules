using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// Writes the collection items of the <c>Into*</c> attributes, and the registrations of the
/// <c>Func</c> constructor parameters of services.
/// </summary>
public class GlueSourceGenerator : BaseAttributeSourceGenerator<GlueClassModel>
{
    private static readonly ITypeDefinition[] _intoAttributeTypes =
    {
        KnownTypes.DependencyModules.Attributes.IntoMapAttribute,
        KnownTypes.DependencyModules.Attributes.IntoSetAttribute,
        KnownTypes.DependencyModules.Attributes.IntoListAttribute,
        KnownTypes.DependencyModules.Attributes.IntoOrderedListAttribute,
    };

    private static readonly ITypeDefinition[] _defaultServiceAttributeTypes =
    {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute,
        KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute,
        KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
        KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute,
    };

    private readonly ITypeDefinition[] _serviceAttributeTypes;
    private readonly HashSet<string> _serviceAttributeNames;
    private readonly IEqualityComparer<GlueClassModel> _comparer = new GlueClassModelComparer();

    public GlueSourceGenerator()
        : this(_defaultServiceAttributeTypes) { }

    /// <param name="serviceAttributeTypes">
    /// The service attributes of the generator. The <c>Func</c> constructor parameters of a class
    /// with one of these attributes get registrations.
    /// </param>
    public GlueSourceGenerator(IEnumerable<ITypeDefinition> serviceAttributeTypes)
    {
        _serviceAttributeTypes = serviceAttributeTypes.ToArray();
        _serviceAttributeNames = new HashSet<string>(
            _serviceAttributeTypes.Select(type => type.Name)
        );
    }

    protected override IEnumerable<ITypeDefinition> AttributeTypes() =>
        _intoAttributeTypes.Concat(_serviceAttributeTypes);

    protected override IEqualityComparer<GlueClassModel> GetComparer() => _comparer;

    protected override GlueClassModel IgnoredModel => GlueClassModel.Ignore;

    protected override GlueClassModel GenerateAttributeModel(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    ) =>
        GlueModelUtility.GetModel(context, _serviceAttributeNames, cancellationToken)
        ?? GlueClassModel.Ignore;

    protected override void GenerateSourceOutput(
        SourceProductionContext context,
        (
            ImmutableArray<(
                ModuleEntryPointModel Left,
                DependencyModuleConfigurationModel Right
            )> Left,
            ImmutableArray<GlueClassModel> Right
        ) inputData,
        FileLogger logger
    )
    {
        var models = Models(inputData.Right);

        if (inputData.Left.Length == 0 || models.Count == 0)
        {
            return;
        }

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(
            inputData.Left
        );

        foreach (var target in EntryModelUtil.RegistrationTargets(entryPointList))
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var entryPointModel = EntryModelUtil.EnsureNamespace(target, configurationModel);
            var items = WritableItems(entryPointModel, models);
            var funcs = Funcs(entryPointModel, models);

            if (items.Count == 0 && funcs.Count == 0)
            {
                continue;
            }

            logger.Info(
                $"Generating glue for {entryPointModel.EntryPointType.Name}: "
                    + $"{items.Count} collection item(s) and {funcs.Count} Func registration(s)."
            );

            context.AddSource(
                entryPointModel.EntryPointType.GetFileNameHint(
                    configurationModel.RootNamespace,
                    "Glue"
                ),
                new GlueFileWriter().Write(entryPointModel, configurationModel, items, funcs)
            );
        }
    }

    protected override void ReportDiagnostics(
        SourceProductionContext context,
        (
            ImmutableArray<(
                ModuleEntryPointModel Left,
                DependencyModuleConfigurationModel Right
            )> Left,
            ImmutableArray<GlueClassModel> Right
        ) data,
        SyntaxTreeLookup lookup,
        FileLogger logger
    )
    {
        var models = Models(data.Right);

        if (data.Left.Length == 0 || models.Count == 0)
        {
            return;
        }

        var parameters = new HashSet<string>();

        foreach (var model in models)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var location = model.Location.ToLocationOrNone(lookup);

            foreach (var item in model.Items)
            {
                var reason = item.Problem ?? model.ConstructionProblem;

                if (reason != null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            DependencyModuleDiagnostics.CollectionItemCannotBeBuilt,
                            location,
                            model.DisplayName,
                            Describe(item),
                            reason
                        )
                    );
                }
            }

            foreach (var problem in model.FuncProblems)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.FuncCannotBeBuilt,
                        location,
                        model.DisplayName,
                        Display(problem.FuncType),
                        problem.Reason
                    )
                );
            }

            foreach (var problem in model.ParameterProblems)
            {
                // More than one class can build the same object.
                if (!parameters.Add(problem.Owner + "|" + problem.Parameter))
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.ParameterCannotBeBuilt,
                        location,
                        problem.Owner,
                        problem.Parameter,
                        problem.ParameterType,
                        problem.Reason
                    )
                );
            }
        }

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(
            data.Left
        );

        // A pair of classes is reported one time, also when more than one module has both.
        var reported = new HashSet<string>();

        foreach (var target in EntryModelUtil.RegistrationTargets(entryPointList))
        {
            var entryPointModel = EntryModelUtil.EnsureNamespace(target, configurationModel);
            var items = WritableItems(entryPointModel, models);

            ReportDuplicateKeys(context, items, lookup, reported);
            ReportMixedOrder(context, items, lookup, reported);
        }
    }

    private static void ReportDuplicateKeys(
        SourceProductionContext context,
        IReadOnlyList<GlueItemOutput> items,
        SyntaxTreeLookup lookup,
        HashSet<string> reported
    )
    {
        var first = new Dictionary<string, GlueItemOutput>();

        foreach (var output in items)
        {
            if (output.Item.Kind != GlueItemKind.Map)
            {
                continue;
            }

            var key = $"{output.Item.KeyType}|{output.Item.ItemType}|{output.Item.Value}";

            if (!first.TryGetValue(key, out var other))
            {
                first[key] = output;
                continue;
            }

            if (
                other.Class.TypeName == output.Class.TypeName
                || !reported.Add($"key|{key}|{output.Class.TypeName}")
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.DuplicateMapKey,
                    output.Class.Location.ToLocationOrNone(lookup),
                    other.Class.DisplayName,
                    output.Class.DisplayName,
                    output.Item.ValueDisplay,
                    $"{Display(output.Item.KeyType!)} to {Display(output.Item.ItemType)}"
                )
            );
        }
    }

    private static void ReportMixedOrder(
        SourceProductionContext context,
        IReadOnlyList<GlueItemOutput> items,
        SyntaxTreeLookup lookup,
        HashSet<string> reported
    )
    {
        var first = new Dictionary<string, GlueItemOutput>();

        foreach (var output in items)
        {
            if (output.Item.Kind is not (GlueItemKind.TypeList or GlueItemKind.ValueList))
            {
                continue;
            }

            if (!first.TryGetValue(output.Item.ItemType, out var other))
            {
                first[output.Item.ItemType] = output;
                continue;
            }

            if (
                Scheme(other.Item) == Scheme(output.Item)
                || !reported.Add($"order|{output.Item.ItemType}|{output.Class.TypeName}")
            )
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.MixedListOrder,
                    output.Class.Location.ToLocationOrNone(lookup),
                    output.Class.DisplayName,
                    Scheme(output.Item),
                    Display(output.Item.ItemType),
                    other.Class.DisplayName,
                    Scheme(other.Item)
                )
            );
        }
    }

    private static List<GlueClassModel> Models(ImmutableArray<GlueClassModel> models)
    {
        var list = new List<GlueClassModel>();

        foreach (var model in models)
        {
            if (!model.IsIgnored)
            {
                list.Add(model);
            }
        }

        // The collector gives the models in the order of its providers. A fixed order keeps the
        // generated file the same between builds.
        list.Sort((x, y) => string.CompareOrdinal(x.TypeName, y.TypeName));

        return list;
    }

    /// <summary>
    /// The items that a module adds: those of its realm that the glue can build.
    /// </summary>
    private static List<GlueItemOutput> WritableItems(
        ModuleEntryPointModel entryPointModel,
        List<GlueClassModel> models
    )
    {
        var items = new List<GlueItemOutput>();

        foreach (var model in models)
        {
            if (model.Construction == null)
            {
                continue;
            }

            foreach (var item in model.Items)
            {
                if (item.Problem == null && InRealm(item.Realm, entryPointModel))
                {
                    items.Add(new GlueItemOutput(model, item));
                }
            }
        }

        return items;
    }

    private static List<GlueFuncModel> Funcs(
        ModuleEntryPointModel entryPointModel,
        List<GlueClassModel> models
    )
    {
        var funcs = new SortedDictionary<string, GlueFuncModel>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            if (!model.FuncRealms.Any(realm => InRealm(realm, entryPointModel)))
            {
                continue;
            }

            foreach (var func in model.Funcs)
            {
                if (!funcs.ContainsKey(func.FuncType))
                {
                    funcs[func.FuncType] = func;
                }
            }
        }

        return funcs.Values.ToList();
    }

    private static bool InRealm(string? realm, ModuleEntryPointModel entryPointModel)
    {
        if (realm == null)
        {
            return !entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.OnlyRealm);
        }

        var type = entryPointModel.EntryPointType;

        return realm
            == (
                string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name
            );
    }

    private static string Scheme(GlueItemModel item) =>
        item.Kind == GlueItemKind.TypeList
            ? "IntoList<T>"
            : $"IntoList<T, {Display(item.KeyType!)}>";

    private static string Describe(GlueItemModel item) =>
        item.Kind switch
        {
            GlueItemKind.Map => $"the map of {Display(item.KeyType!)} to {Display(item.ItemType)}",
            GlueItemKind.Set => $"the set of {Display(item.ItemType)}",
            _ => $"the list of {Display(item.ItemType)}",
        };

    private static string Display(string typeName) => typeName.Replace("global::", "");
}

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
    /// One provider per attribute, merged.
    /// </summary>
    /// <remarks>
    /// <c>ForAttributeWithMetadataName</c> takes a single name, so an attribute set needs one
    /// provider each. They share Roslyn's attribute index, so several indexed lookups still cost far
    /// less than the one visit of every syntax node this replaced.
    /// </remarks>
    private IncrementalValueProvider<ImmutableArray<T>> CollectModels(
        IncrementalGeneratorInitializationContext context, ITypeDefinition[] attributeTypes) {

        IncrementalValueProvider<ImmutableArray<T>>? merged = null;

        foreach (var attributeType in attributeTypes) {
            var owner = attributeType;

            var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
                    MetadataName(owner),
                    static (node, _) => node is MemberDeclarationSyntax,
                    (syntaxContext, cancellation) =>
                        GenerateOwnedModel(syntaxContext, cancellation, attributeTypes, owner))
                .WithComparer(GetComparer())
                .Collect();

            merged = merged == null
                ? provider
                : merged.Value.Combine(provider).Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        return merged!.Value;
    }

    /// <summary>
    /// Builds the model only from the provider that owns the declaration.
    /// </summary>
    /// <remarks>
    /// A declaration carrying two of these attributes — <c>[SingletonService] [CrossWireService]</c>
    /// is a supported pair — is produced by two providers. The model is built from the whole
    /// declaration rather than from the attribute that triggered it, so both would be identical and
    /// every registration would be emitted twice. The first attribute present, in the order the
    /// generator declares them, is the one that builds it.
    /// </remarks>
    private T GenerateOwnedModel(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellation,
        ITypeDefinition[] attributeTypes,
        ITypeDefinition owner) {

        var present = context.TargetSymbol.GetAttributes();

        foreach (var candidate in attributeTypes) {
            if (!IsPresent(present, candidate)) {
                continue;
            }

            return candidate.Equals(owner) ? GenerateAttributeModel(context, cancellation) : IgnoredModel;
        }

        return GenerateAttributeModel(context, cancellation);
    }

    private static bool IsPresent(ImmutableArray<AttributeData> present, ITypeDefinition candidate) {
        foreach (var attribute in present) {
            if (attribute.AttributeClass is { } attributeClass &&
                attributeClass.Name == candidate.Name &&
                NamespaceOf(attributeClass) == candidate.Namespace) {
                return true;
            }
        }

        return false;
    }

    private static string NamespaceOf(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : "";

    private static string MetadataName(ITypeDefinition attributeType) =>
        string.IsNullOrEmpty(attributeType.Namespace)
            ? attributeType.Name
            : attributeType.Namespace + "." + attributeType.Name;

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
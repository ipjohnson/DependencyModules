using System.Collections.Immutable;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Builds one collected provider from a set of attributes, indexed rather than scanned.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>BaseAttributeSourceGenerator</c> so a generator can build more than one of
/// these. The decorator generator needs two — its own attributes, and the service attributes, because
/// monomorphising a generic decorator means emitting one call per closed registration and the
/// registrations are what say which closings exist.
/// </para>
/// <para>
/// Cheap enough to do twice: <c>ForAttributeWithMetadataName</c> shares Roslyn's attribute index, and
/// a second set of providers over it measured 3.5 ms cold and 0.1 ms per keystroke on a 2,000-class
/// compilation — against 33 ms for the one visit of every syntax node this shape replaced.
/// </para>
/// </remarks>
public static class AttributeModelCollector {

    /// <summary>
    /// Collects one model per declaration carrying any of <paramref name="attributeTypes"/>.
    /// </summary>
    /// <param name="generate">Builds the model from the declaration the attribute was found on.</param>
    /// <param name="ignored">
    /// The sentinel returned for a declaration this provider does not own. Every model type in this
    /// codebase has one and every writer already skips it.
    /// </param>
    public static IncrementalValueProvider<ImmutableArray<TModel>> Collect<TModel>(
        IncrementalGeneratorInitializationContext context,
        ITypeDefinition[] attributeTypes,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, TModel> generate,
        IEqualityComparer<TModel> comparer,
        TModel ignored) {

        IncrementalValueProvider<ImmutableArray<TModel>>? merged = null;

        // ForAttributeWithMetadataName takes a single name, so an attribute set needs one provider
        // each. They share the index, so several indexed lookups still cost far less than one visit
        // of every syntax node.
        foreach (var attributeType in attributeTypes) {
            var owner = attributeType;

            var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
                    MetadataName(owner),
                    static (node, _) => node is MemberDeclarationSyntax,
                    (syntaxContext, cancellation) =>
                        Owned(syntaxContext, cancellation, attributeTypes, owner, generate, ignored))
                .WithComparer(comparer)
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
    private static TModel Owned<TModel>(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellation,
        ITypeDefinition[] attributeTypes,
        ITypeDefinition owner,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, TModel> generate,
        TModel ignored) {

        var present = context.TargetSymbol.GetAttributes();

        foreach (var candidate in attributeTypes) {
            if (!IsPresent(present, candidate)) {
                continue;
            }

            return candidate.Equals(owner) ? generate(context, cancellation) : ignored;
        }

        return generate(context, cancellation);
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
}

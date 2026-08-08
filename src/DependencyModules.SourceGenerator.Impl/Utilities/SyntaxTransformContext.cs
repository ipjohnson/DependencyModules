using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// The node being transformed and the semantic model to read it with, independent of which provider
/// produced it.
/// </summary>
/// <remarks>
/// Roslyn hands a different context type to each kind of provider —
/// <see cref="GeneratorSyntaxContext"/> from <c>CreateSyntaxProvider</c> and
/// <see cref="GeneratorAttributeSyntaxContext"/> from <c>ForAttributeWithMetadataName</c> — and
/// neither is publicly constructible, so a helper written against one cannot be reused by the other.
/// Everything below the provider takes this instead, and the implicit conversions keep the call sites
/// identical whichever provider they came from.
/// </remarks>
public readonly struct SyntaxTransformContext {

    public SyntaxTransformContext(SyntaxNode node, SemanticModel semanticModel) {
        Node = node;
        SemanticModel = semanticModel;
    }

    public SyntaxNode Node { get; }

    public SemanticModel SemanticModel { get; }

    public static implicit operator SyntaxTransformContext(GeneratorSyntaxContext context) =>
        new(context.Node, context.SemanticModel);

    /// <summary>
    /// The target node is the declaration the attribute was found on, which is the node a
    /// <c>CreateSyntaxProvider</c> predicate would have selected for the same attribute.
    /// </summary>
    public static implicit operator SyntaxTransformContext(GeneratorAttributeSyntaxContext context) =>
        new(context.TargetNode, context.SemanticModel);
}

using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Whether an attribute usage is a given attribute type, resolved rather than string-matched.
/// </summary>
/// <remarks>
/// <para>
/// Attribute usages were compared as written — <c>attributeSyntax.Name.ToString()</c> against the
/// type's simple name, and against that name with <c>Attribute</c> appended. Every other legal
/// spelling missed, and missing meant the registration was silently absent: no diagnostic, a green
/// build, and a failure at the first resolve.
/// </para>
/// <para>
/// <c>[SingletonService]</c> and <c>[SingletonServiceAttribute]</c> matched.
/// <c>[DependencyModules.Runtime.Attributes.SingletonService]</c>,
/// <c>[global::DependencyModules.Runtime.Attributes.SingletonServiceAttribute]</c> and any
/// <c>using</c> alias did not.
/// </para>
/// <para>
/// This belongs in a transform, never in a predicate: it reads the semantic model, which is what
/// resolves an alias and a qualified name to the same symbol, and what a predicate visiting every
/// node in the compilation must not do.
/// </para>
/// </remarks>
public static class AttributeTypeMatcher {

    /// <summary>
    /// Whether <paramref name="attributeSyntax"/> resolves to <paramref name="attributeType"/>.
    /// </summary>
    /// <remarks>
    /// Falls back to comparing the written name when the symbol cannot be resolved, which happens
    /// while a file is mid-edit and the attribute does not yet bind. Answering "no" there would make
    /// registrations flicker out of the container between keystrokes.
    /// </remarks>
    public static bool Matches(
        SemanticModel semanticModel,
        AttributeSyntax attributeSyntax,
        ITypeDefinition attributeType,
        CancellationToken cancellationToken) {

        var symbol = Resolve(semanticModel, attributeSyntax, cancellationToken);

        if (symbol == null) {
            return MatchesAsWritten(attributeSyntax, attributeType);
        }

        return symbol.Name == attributeType.Name && NamespaceOf(symbol) == attributeType.Namespace;
    }

    /// <summary>
    /// The attribute class an attribute usage names.
    /// </summary>
    /// <remarks>
    /// An attribute usage binds to a constructor, so the type is that constructor's containing type.
    /// <c>GetTypeInfo</c> answers for the cases where the constructor could not be chosen — an
    /// argument list that does not match any overload still names the attribute unambiguously.
    /// </remarks>
    private static INamedTypeSymbol? Resolve(
        SemanticModel semanticModel, AttributeSyntax attributeSyntax, CancellationToken cancellationToken) {

        var symbolInfo = semanticModel.GetSymbolInfo(attributeSyntax, cancellationToken);

        if (symbolInfo.Symbol?.ContainingType is { } containingType) {
            return containingType;
        }

        if (symbolInfo.CandidateSymbols.Length > 0 &&
            symbolInfo.CandidateSymbols[0].ContainingType is { } candidateType) {
            return candidateType;
        }

        return semanticModel.GetTypeInfo(attributeSyntax, cancellationToken).Type as INamedTypeSymbol;
    }

    /// <summary>
    /// The old comparison, kept only for the unresolvable case.
    /// </summary>
    private static bool MatchesAsWritten(AttributeSyntax attributeSyntax, ITypeDefinition attributeType) {
        var written = attributeSyntax.Name.ToString();
        var lastDot = written.LastIndexOf('.');

        if (lastDot >= 0) {
            written = written.Substring(lastDot + 1);
        }

        return written == attributeType.Name || written + "Attribute" == attributeType.Name;
    }

    private static string NamespaceOf(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : "";
}

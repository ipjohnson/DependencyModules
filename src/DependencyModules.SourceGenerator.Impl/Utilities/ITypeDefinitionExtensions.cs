using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class ITypeDefinitionExtensions {

    /// <summary>
    /// Rewrites a generic type's arguments to nothing, so it renders as <c>IRepo&lt;&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <c>typeof(IRepo&lt;&gt;)</c> resolves to the unbound symbol, whose <c>TypeArguments</c> are
    /// the declaration's type <i>parameters</i> — so rendering it verbatim produces
    /// <c>typeof(IRepo&lt;T&gt;)</c>, and <c>T</c> means nothing where the attribute is re-emitted.
    /// The unbound form is the only legal way to write it, and this is what produces it.
    /// </remarks>
    public static ITypeDefinition ToUnboundGeneric(this ITypeDefinition type) =>
        type is GenericTypeDefinition { TypeArguments.Count: > 0 } generic
            ? new GenericTypeDefinition(
                generic.TypeDefinitionEnum,
                generic.Namespace,
                generic.Name,
                generic.TypeArguments.Select(_ => (ITypeDefinition)TypeDefinition.Get("", "")).ToArray())
            : type;

    public static string GetFileNameHint(this ITypeDefinition typeDefinition, string rootNamespace, string uniquePart) {
        var nameString = typeDefinition.Namespace;
        
        if (nameString == rootNamespace ||
            nameString.StartsWith(rootNamespace + ".")) {
            nameString = nameString.Substring(rootNamespace.Length);
            nameString = nameString.TrimStart('.');
        }

        if (!string.IsNullOrWhiteSpace(nameString)) {
            nameString += ".";
        }
        
        return $"{nameString}{typeDefinition.Name}.{uniquePart}.g.cs";
    }
}
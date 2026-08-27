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

    /// <summary>
    /// The hint name a generated file is added under. Must differ for types that differ, because a
    /// repeat is an exception inside the generator rather than a diagnostic — it surfaces as CS8785,
    /// a warning, and then as errors against the developer's own code.
    /// </summary>
    /// <remarks>
    /// The RootNamespace prefix is dropped so the common case reads as <c>Thing.Module.g.cs</c>
    /// rather than repeating the project's namespace in every file. That leaves one pair of distinct
    /// types sharing a name: a type in the root namespace, whose prefix is stripped to nothing, and
    /// a type in the global namespace, which had none to begin with. They are different types and
    /// need different files, so the global namespace is named rather than left blank.
    /// </remarks>
    public static string GetFileNameHint(this ITypeDefinition typeDefinition, string rootNamespace, string uniquePart) {
        var nameString = typeDefinition.Namespace;

        if (nameString == rootNamespace ||
            nameString.StartsWith(rootNamespace + ".")) {
            nameString = nameString.Substring(rootNamespace.Length);
            nameString = nameString.TrimStart('.');
        }
        else if (string.IsNullOrWhiteSpace(nameString)) {
            nameString = "global";
        }

        if (!string.IsNullOrWhiteSpace(nameString)) {
            nameString += ".";
        }

        return $"{nameString}{typeDefinition.Name}.{uniquePart}.g.cs";
    }
}
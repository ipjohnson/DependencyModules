using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class ITypeDefinitionExtensions
{
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
                generic
                    .TypeArguments.Select(_ => (ITypeDefinition)TypeDefinition.Get("", ""))
                    .ToArray()
            )
            : type;

    /// <summary>
    /// The hint name a generated file is added under. Must differ for types that differ, because a
    /// repeat is an exception inside the generator rather than a diagnostic — it surfaces as CS8785,
    /// a warning, and then as errors against the developer's own code.
    /// </summary>
    /// <remarks>
    /// The RootNamespace prefix is dropped so the common case reads as <c>Thing.Module.g.cs</c>
    /// rather than repeating the project's namespace in every file. A type outside the root
    /// namespace, the global namespace included, keeps its full name behind <c>global-</c>. Without
    /// that mark, <c>Root.Sub.Thing</c> and <c>Sub.Thing</c> both become <c>Sub.Thing</c>. A
    /// namespace cannot contain '-', so no stripped name can take that form.
    /// </remarks>
    public static string GetFileNameHint(
        this ITypeDefinition typeDefinition,
        string rootNamespace,
        string uniquePart
    )
    {
        var typeNamespace = typeDefinition.Namespace;

        var fullName = string.IsNullOrEmpty(typeNamespace)
            ? typeDefinition.Name
            : typeNamespace + "." + typeDefinition.Name;

        string name;

        if (string.IsNullOrEmpty(rootNamespace))
        {
            name = fullName;
        }
        else if (typeNamespace == rootNamespace)
        {
            name = typeDefinition.Name;
        }
        else if (typeNamespace.StartsWith(rootNamespace + "."))
        {
            name = fullName.Substring(rootNamespace.Length + 1);
        }
        else
        {
            name = "global-" + fullName;
        }

        return $"{name}.{uniquePart}.g.cs";
    }
}

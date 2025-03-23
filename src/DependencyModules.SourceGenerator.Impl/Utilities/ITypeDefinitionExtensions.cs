using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class ITypeDefinitionExtensions {
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
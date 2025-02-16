using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class SyntaxNodeExtensions {
    public static string GetNamespace(this BaseTypeDeclarationSyntax syntax) {
        var parentSyntaxNode = syntax.Parent;

        while (parentSyntaxNode != null &&
               parentSyntaxNode is not NamespaceDeclarationSyntax &&
               parentSyntaxNode is not FileScopedNamespaceDeclarationSyntax) {
            parentSyntaxNode = parentSyntaxNode.Parent;
        }

        if (parentSyntaxNode is BaseNamespaceDeclarationSyntax namespaceNode) {
            return WalkNamespaceNodes(namespaceNode);
        }

        return "";
    }

    private static string WalkNamespaceNodes(BaseNamespaceDeclarationSyntax? namespaceNode) {
        var stringBuilder = new StringBuilder();

        while (namespaceNode != null) {
            if (stringBuilder.Length > 0) {
                stringBuilder.Insert(0, '.');
            }

            stringBuilder.Insert(0, namespaceNode.Name);

            namespaceNode = namespaceNode.Parent as BaseNamespaceDeclarationSyntax;
        }

        return stringBuilder.ToString();
    }

    public static ITypeDefinition GetTypeDefinition(this ClassDeclarationSyntax classDeclarationSyntax) {
        var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        return TypeDefinition.Get(namespaceSyntax?.Name.ToFullString().TrimEnd() ?? "",
            classDeclarationSyntax.Identifier.Text);
    }

    public static AttributeSyntax? GetAttribute(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().FirstOrDefault(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static IEnumerable<AttributeSyntax>
        GetAttributes(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().Where(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static bool IsAttributed(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().Any(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static bool IsAttributed(this SyntaxNode node, ITypeDefinition typeDefinition) {
        var ns = typeDefinition.Namespace;
        var attributeName = typeDefinition.Name.Replace("Attribute", "");

        return IsAttributed(node, attributeName, ns);
    }
}
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class SyntaxSelector<T> where T : SyntaxNode {
    private const string _attributeString = "Attribute";
    private readonly List<string> _names;

    public SyntaxSelector(params ITypeDefinition[] attributes) {
        _names = GetAttributeStrings(attributes);
    }

    private List<string> GetAttributeStrings(ITypeDefinition[] attributes) {
        var returnList = new List<string>();

        foreach (var attribute in attributes) {
            returnList.Add( attribute.Name );
            returnList.Add( attribute.Namespace + "." + attribute.Name);
            
            if (attribute.Name.EndsWith(_attributeString)) {
                var simpleName = attribute.Name.Substring(0, attribute.Name.Length - _attributeString.Length);

                returnList.Add(simpleName);
            }
        }
        
        return returnList;
    }

    public bool Where(SyntaxNode node, CancellationToken token) {
        if (node is not T) {
            return false;
        }

        var found = node.DescendantNodes()
            .OfType<AttributeSyntax>().Any(a => {
                var name = a.Name.ToString();
                return _names.Contains(name);
            });

        return found;
    }
}
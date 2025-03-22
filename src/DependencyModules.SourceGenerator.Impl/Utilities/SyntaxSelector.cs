using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public abstract class BaseSyntaxSelector {
        private const string _attributeString = "Attribute";
    private readonly List<string> _names;

    public bool AutoApproveCompilationUnit { get; set; } = false;
    
    public string ApproveFilter { get; set; } = "";
    
    protected BaseSyntaxSelector(params ITypeDefinition[] attributes) {
        _names = GetAttributeStrings(attributes);
    }

    private List<string> GetAttributeStrings(ITypeDefinition[] attributes) {
        var returnList = new List<string>();

        foreach (var attribute in attributes) {
            returnList.Add(attribute.Name);
            returnList.Add(attribute.Namespace + "." + attribute.Name);

            if (attribute.Name.EndsWith(_attributeString)) {
                var simpleName = attribute.Name.Substring(0, attribute.Name.Length - _attributeString.Length);

                returnList.Add(simpleName);
            }
        }

        return returnList;
    }

    protected abstract bool TestForTypes(SyntaxNode node, CancellationToken token);
    
    public bool Where(SyntaxNode node, CancellationToken token) {
        
        if (!TestForTypes(node, token)) {
            return false;
        }

        if (node is MemberDeclarationSyntax memberDeclarationSyntax) {
            return ProcessAttributeList(memberDeclarationSyntax.AttributeLists);
        }

        if (node is CompilationUnitSyntax compilationUnitSyntax) {
            return IsAutoApprove(compilationUnitSyntax) ||
                   ProcessAttributeList(compilationUnitSyntax.AttributeLists);
        }
        
        var found = node.DescendantNodes()
            .OfType<AttributeSyntax>().Any(a => {
                var name = a.Name.ToString();
                return _names.Contains(name);
            });
        
        return found;
    }

    private bool IsAutoApprove(CompilationUnitSyntax compilationUnitSyntax) {
        if (!AutoApproveCompilationUnit) {
            return false;
        }
        
        return ApproveFilter == "" || 
               compilationUnitSyntax.SyntaxTree.FilePath.EndsWith(ApproveFilter);
    }

    private bool ProcessAttributeList(SyntaxList<AttributeListSyntax> attributeLists) {
        var foundAttribute = false;
        foreach (var attributeListSyntax in attributeLists) {
            foreach (var attributeSyntax in attributeListSyntax.Attributes) {
                var name = attributeSyntax.Name.ToString();
                    
                foundAttribute = _names.Contains(name);
                    
                if (foundAttribute) {
                    break;
                }
            }
            if (foundAttribute) {
                break;
            }
        }
            
        return foundAttribute;
    }

}

public class SyntaxSelector<T> : BaseSyntaxSelector where T : SyntaxNode {
    public SyntaxSelector(params ITypeDefinition[] attributes) : base(attributes) {}
    
    protected override bool TestForTypes(SyntaxNode node, CancellationToken token) {
        if (node is T) {
            return true;
        }
        
        return false;
    }
}


public class SyntaxSelector<T1,T2> : BaseSyntaxSelector where T1 : SyntaxNode where T2 : SyntaxNode {
    public SyntaxSelector(params ITypeDefinition[] attributes) : base(attributes) {}
    
    protected override bool TestForTypes(SyntaxNode node, CancellationToken token) {
        if (node is T1 or T2) {
            return true;
        }
        
        return false;
    }
}
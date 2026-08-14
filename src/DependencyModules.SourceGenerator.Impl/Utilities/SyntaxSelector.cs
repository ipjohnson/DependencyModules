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

    /// <summary>
    /// The bare names an attribute may be written as, with and without the <c>Attribute</c> suffix.
    /// </summary>
    /// <remarks>
    /// Qualification is stripped from the usage rather than enumerated here — see
    /// <see cref="LastSegment"/>. Listing prefixes was the previous approach and it missed the
    /// namespace-qualified form without the suffix, so
    /// <c>[DependencyModules.Runtime.Attributes.DependencyModule]</c> — valid C# — was silently not a
    /// module: no partial written, no diagnostic, and a CS0311 at the consumer's
    /// <c>AddModule&lt;T&gt;()</c> naming neither the attribute nor the omission.
    /// </remarks>
    private List<string> GetAttributeStrings(ITypeDefinition[] attributes) {
        var returnList = new List<string>();

        foreach (var attribute in attributes) {
            returnList.Add(attribute.Name);

            if (attribute.Name.EndsWith(_attributeString)) {
                var simpleName = attribute.Name.Substring(0, attribute.Name.Length - _attributeString.Length);

                returnList.Add(simpleName);
            }
        }

        return returnList;
    }

    /// <summary>
    /// An attribute usage reduced to the name it ends in, so every way of qualifying it compares
    /// equal.
    /// </summary>
    /// <remarks>
    /// Covers <c>Ns.Attr</c>, <c>global::Ns.Attr</c> and <c>alias::Ns.Attr</c>. It does not cover a
    /// <c>using</c> alias of the attribute type itself, which resolves only through the semantic
    /// model and so cannot be seen from a predicate that must stay syntax-only.
    ///
    /// This does not widen what matches by namespace: the bare simple name was already accepted
    /// regardless of which namespace it came from, so a same-named attribute from elsewhere was
    /// always a candidate and is filtered downstream as it always was.
    /// </remarks>
    private static string LastSegment(string attributeName) {
        var lastDot = attributeName.LastIndexOf('.');

        if (lastDot >= 0) {
            return attributeName.Substring(lastDot + 1);
        }

        var lastColon = attributeName.LastIndexOf(':');

        return lastColon >= 0 ? attributeName.Substring(lastColon + 1) : attributeName;
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
            .OfType<AttributeSyntax>().Any(a => _names.Contains(LastSegment(a.Name.ToString())));
        
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
                foundAttribute = _names.Contains(LastSegment(attributeSyntax.Name.ToString()));
                    
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

public class SyntaxSelector<T1,T2,T3> : BaseSyntaxSelector where T1 : SyntaxNode where T2 : SyntaxNode where T3 : SyntaxNode {
    public SyntaxSelector(params ITypeDefinition[] attributes) : base(attributes) {}

    protected override bool TestForTypes(SyntaxNode node, CancellationToken token) {
        if (node is T1 or T2 or T3) {
            return true;
        }

        return false;
    }
}
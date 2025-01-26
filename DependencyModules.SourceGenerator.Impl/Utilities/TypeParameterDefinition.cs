using System.Text;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class TypeParameterDefinition : ITypeDefinition {
    public TypeParameterDefinition(TypeDefinitionEnum typeDefinitionEnum, bool isNullable, bool isArray, string name) {
        TypeDefinitionEnum = typeDefinitionEnum;
        IsNullable = isNullable;
        IsArray = isArray;
        Name = name;
    }

    public int CompareTo(ITypeDefinition other) {
        if (other is TypeParameterDefinition tpd) {
            return tpd.Name == Name ? 0 : 1;
        }
        
        return -1;
    }

    public TypeDefinitionEnum TypeDefinitionEnum {
        get;
    }

    public bool IsNullable {
        get;
    }

    public bool IsArray {
        get;
    }

    public string Name {
        get;
    }

    public string Namespace => "";

    public IEnumerable<string> KnownNamespaces => Enumerable.Empty<string>();

    public void WriteShortName(StringBuilder builder) {
        builder.Append(Name);
    }

    public ITypeDefinition MakeNullable(bool nullable = true) {
        return new TypeParameterDefinition(TypeDefinitionEnum, nullable, IsArray, Name);
    }

    public ITypeDefinition MakeArray() {
        return new TypeParameterDefinition(TypeDefinitionEnum, IsNullable, true, Name);
    }

    public IReadOnlyList<ITypeDefinition> TypeArguments => Array.Empty<ITypeDefinition>();
}
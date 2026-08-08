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

    /// <summary>
    /// Value equality, so a model holding one compares equal across runs.
    /// </summary>
    /// <remarks>
    /// Without this the incremental cache misses on every keystroke for any member with a type
    /// parameter, because the default comparer falls back to reference equality and a fresh
    /// instance is built each run.
    /// </remarks>
    public override bool Equals(object obj) =>
        obj is TypeParameterDefinition other &&
        other.Name == Name &&
        other.IsNullable == IsNullable &&
        other.IsArray == IsArray;

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();

            hash = hash * 31 + IsNullable.GetHashCode();
            hash = hash * 31 + IsArray.GetHashCode();

            return hash;
        }
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
    
    public void WriteTypeName(StringBuilder builder, TypeOutputMode typeOutputMode = TypeOutputMode.ShortName) {
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
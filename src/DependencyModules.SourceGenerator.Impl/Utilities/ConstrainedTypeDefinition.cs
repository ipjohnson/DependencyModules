using System.Text;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// A base type that carries the generic constraints of the class declaring it.
/// </summary>
/// <remarks>
/// A generic state class has to repeat the constraints of the member it was generated for, or the
/// call it forwards will not satisfy them. A class is written as <c>class {Name} : {baseTypes}</c>,
/// so the clause travels with the base type, which is exactly where it belongs in the output:
/// <c>class DmState0&lt;T&gt; : InvocationState&lt;T&gt; where T : class</c>.
/// </remarks>
public class ConstrainedTypeDefinition : ITypeDefinition {
    private readonly ITypeDefinition _baseType;
    private readonly string _constraints;

    /// <param name="baseType">The type being derived from.</param>
    /// <param name="constraints">The clause to append, beginning with <c>where</c>.</param>
    public ConstrainedTypeDefinition(ITypeDefinition baseType, string constraints) {
        _baseType = baseType;
        _constraints = constraints;
    }

    public TypeDefinitionEnum TypeDefinitionEnum => _baseType.TypeDefinitionEnum;

    public bool IsNullable => _baseType.IsNullable;

    public bool IsArray => _baseType.IsArray;

    public string Name => _baseType.Name;

    public string Namespace => _baseType.Namespace;

    public IEnumerable<string> KnownNamespaces => _baseType.KnownNamespaces;

    public IReadOnlyList<ITypeDefinition> TypeArguments => _baseType.TypeArguments;

    public void WriteTypeName(StringBuilder builder, TypeOutputMode typeOutputMode = TypeOutputMode.ShortName) {
        _baseType.WriteTypeName(builder, typeOutputMode);

        builder.Append(' ');
        builder.Append(_constraints);
    }

    public ITypeDefinition MakeNullable(bool nullable = true) =>
        new ConstrainedTypeDefinition(_baseType.MakeNullable(nullable), _constraints);

    public ITypeDefinition MakeArray() =>
        new ConstrainedTypeDefinition(_baseType.MakeArray(), _constraints);

    public int CompareTo(ITypeDefinition other) {
        if (other is not ConstrainedTypeDefinition constrained) {
            return -1;
        }

        var baseCompare = _baseType.CompareTo(constrained._baseType);

        return baseCompare != 0
            ? baseCompare
            : string.Compare(_constraints, constrained._constraints, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is ITypeDefinition other && CompareTo(other) == 0;

    public override int GetHashCode() {
        unchecked {
            return _baseType.GetHashCode() * 31 + _constraints.GetHashCode();
        }
    }
}

using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record ParameterInfoModel(
    string ParameterName,
    ITypeDefinition ParameterType,
    object? DefaultValue,
    IReadOnlyList<AttributeModel> Attributes) {

    // Structural equality over Attributes; see ModelEquality.
    public virtual bool Equals(ParameterInfoModel? other) =>
        other is not null &&
        ParameterName == other.ParameterName &&
        ParameterType.Equals(other.ParameterType) &&
        Equals(DefaultValue, other.DefaultValue) &&
        ModelEquality.ListEquals(Attributes, other.Attributes);

    public override int GetHashCode() {
        unchecked {
            var hash = ParameterName.GetHashCode();
            hash = hash * 31 + ParameterType.GetHashCode();
            hash = hash * 31 + (DefaultValue?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(Attributes);
            return hash;
        }
    }
}
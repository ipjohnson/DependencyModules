namespace DependencyModules.SourceGenerator.Impl.Models;

public record ConstructorInfoModel(IReadOnlyList<ParameterInfoModel> Parameters) {

    // Structural equality over Parameters; see ModelEquality.
    public virtual bool Equals(ConstructorInfoModel? other) =>
        other is not null && ModelEquality.ListEquals(Parameters, other.Parameters);

    public override int GetHashCode() => ModelEquality.ListHashCode(Parameters);
}
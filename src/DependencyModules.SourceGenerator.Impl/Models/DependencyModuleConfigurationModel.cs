namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
///     This is a configuration model for dependency generation
///     the values are intended to come from csproj
/// </summary>
public record DependencyModuleConfigurationModel(
    RegistrationType RegistrationType,
    bool RegisterSourceGenerator,
    string RootNamespace,
    string ProjectDir,
    bool AutoGenerateEntry
);

public class DependencyModuleConfigurationModelComparer :
    IEqualityComparer<DependencyModuleConfigurationModel> {

    public bool Equals(DependencyModuleConfigurationModel? x, DependencyModuleConfigurationModel? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.RegistrationType == y.RegistrationType && 
               x.RegisterSourceGenerator == y.RegisterSourceGenerator &&
               x.RootNamespace == y.RootNamespace &&
               x.AutoGenerateEntry == y.AutoGenerateEntry;
    }

    public int GetHashCode(DependencyModuleConfigurationModel obj) {
        return obj.GetHashCode();
    }
}
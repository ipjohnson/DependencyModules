namespace DependencyModules.SourceGenerator.Impl.Models;

public enum LogOutputLevel {
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}

/// <summary>
///     This is a configuration model for dependency generation
///     the values are intended to come from csproj
/// </summary>
public record DependencyModuleConfigurationModel(
    RegistrationType RegistrationType,
    bool RegisterSourceGenerator,
    string RootNamespace,
    string ProjectDir,
    bool AutoGenerateEntry,
    string LogOutputFolder,
    LogOutputLevel LogOutputLevel,
    bool GenerateFactories
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
               x.ProjectDir == y.ProjectDir &&
               x.LogOutputFolder == y.LogOutputFolder &&
               x.AutoGenerateEntry == y.AutoGenerateEntry &&
               x.LogOutputLevel == y.LogOutputLevel &&
               x.GenerateFactories == y.GenerateFactories;
    }

    public int GetHashCode(DependencyModuleConfigurationModel obj) {
        return obj.GetHashCode();
    }
}
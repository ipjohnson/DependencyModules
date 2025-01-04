namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// This is a configuration model for dependency generation
/// the values are intended to come from csproj
/// </summary>
public record DependencyModuleConfigurationModel(
    bool DefaultUseTry
    );
namespace DependencyModules.Runtime.Interfaces;

/// <summary>
/// Internal interface not intended to be consumed by developers
/// </summary>
public interface IDependencyModuleProvider {
    IDependencyModule GetModule();
}
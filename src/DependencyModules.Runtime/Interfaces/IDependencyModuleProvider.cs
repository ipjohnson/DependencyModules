namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     Internal interface not intended to be consumed by developers
/// </summary>
public interface IDependencyModuleProvider {
    /// <summary>
    /// Retrieves an instance of a dependency module.
    /// </summary>
    /// <returns>
    /// An instance of the <c>IDependencyModule</c>.
    /// </returns>
    IDependencyModule GetModule();
}
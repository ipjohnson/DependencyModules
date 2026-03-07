namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     Minimal environment interface for conditional service registration.
/// </summary>
public interface IModuleEnvironment {
    /// <summary>
    /// The name of the current environment (e.g. "Development", "Production").
    /// </summary>
    string EnvironmentName { get; }

    /// <summary>
    /// Retrieve an environment value by name, returning null if not found.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    string? Value(string name);
}

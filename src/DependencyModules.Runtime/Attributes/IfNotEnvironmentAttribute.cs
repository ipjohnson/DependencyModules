namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Registers the service except when the environment name matches one of the given names.
/// </summary>
/// <remarks>
/// The inverse of <see cref="IfEnvironmentAttribute"/>, and the shape most developer tooling wants:
/// register everywhere but production.
/// <example>
/// <code>
/// [SingletonService]
/// [IfNotEnvironment("Production")]
/// public class RequestProfiler : IRequestProfiler { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class IfNotEnvironmentAttribute : Attribute {
    /// <summary>
    /// Registers the service except in the given environments.
    /// </summary>
    /// <param name="environmentNames">The environment names to exclude.</param>
    public IfNotEnvironmentAttribute(params string[] environmentNames) {
        EnvironmentNames = environmentNames;
    }

    /// <summary>
    /// The environment names this registration is excluded from.
    /// </summary>
    public string[] EnvironmentNames { get; }
}

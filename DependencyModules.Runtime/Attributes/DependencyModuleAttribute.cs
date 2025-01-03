namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Applied to partial classes to denote a module entry point
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DependencyModuleAttribute : Attribute {
    /// <summary>
    /// Only get registration for this specific module
    /// </summary>
    public bool OnlyRealm { get; set; } = false;
}
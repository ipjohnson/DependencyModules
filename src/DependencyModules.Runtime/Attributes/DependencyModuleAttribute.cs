namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Applied to partial classes to denote a module entry point
/// </summary>
public class DependencyModuleAttribute : Attribute {
    /// <summary>
    ///     Only get registration for this specific module
    /// </summary>
    public bool OnlyRealm { get; set; } = false;

    /// <summary>
    ///     Use try when registering, default is false
    /// </summary>
    public bool UseTry { get; set; }
    
    /// <summary>
    /// Generate Module attribute, true by default
    /// </summary>
    public bool GenerateAttribute { get; set; }
}
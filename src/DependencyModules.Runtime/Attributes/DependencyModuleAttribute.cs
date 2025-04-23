namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Applied to partial classes to denote a module entry point
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly , Inherited = false)]
public class DependencyModuleAttribute : Attribute {
    /// <summary>
    ///     Restrict registration to types that are registered for this realm (Type)
    /// </summary>
    public bool OnlyRealm { get; set; } = false;

    /// <summary>
    ///     Use try when registering, default is false
    /// </summary>
    public RegistrationType Using { get; set; } = RegistrationType.Add;

    /// <summary>
    /// Generate Module attribute, true by default
    /// </summary>
    public bool GenerateAttribute { get; set; } = true;

    /// <summary>
    /// Register JsonSourceGenerationOptions classes
    /// </summary>
    public bool RegisterJsonSerializers { get; set; } = false;
    
    /// <summary>
    /// Generate a IServiceCollection extension method
    /// Attributes are usually preferred over UseXXX methods
    /// </summary>
    public string? UseMethod { get; set; }
    
    /// <summary>
    /// Setting this to true will generate registration using factories
    /// instead of allowing the container to construct the type
    /// </summary>
    public bool GenerateFactories { get; set; } = false;
}
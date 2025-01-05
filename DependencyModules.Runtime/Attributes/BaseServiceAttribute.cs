namespace DependencyModules.Runtime.Attributes;

public abstract class BaseServiceAttribute : Attribute {
    /// <summary>
    ///     Key to use for DI registration
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    ///     Service type to register
    /// </summary>
    public Type? ServiceType { get; set; }

    /// <summary>
    ///     If try type will only be registered if there is no pre-existing service
    ///     False by default
    /// </summary>
    public bool UseTry { get; set; }

    /// <summary>
    ///     DependencyModule realm that this type should be associated with
    /// </summary>
    public Type? Realm { get; set; }
}
namespace DependencyModules.Runtime.Attributes;

public enum RegistrationType {
    Add,
    Try,
    TryEnumerable,
    Replace
}

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
    ///     Which method type to use, 
    /// </summary>
    public RegistrationType With { get; set; } = RegistrationType.Add;

    /// <summary>
    ///     DependencyModule realm that this type should be associated with
    /// </summary>
    public Type? Realm { get; set; }
}
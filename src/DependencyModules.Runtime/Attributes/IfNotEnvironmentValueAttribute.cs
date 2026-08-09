namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Registers the service except when the environment carries a given value.
/// </summary>
/// <remarks>
/// The inverse of <see cref="IfEnvironmentValueAttribute"/>. With one argument the service is
/// skipped when the key is present at all; with two, only when it equals the given value.
/// <example>
/// <code>
/// [SingletonService]
/// [IfNotEnvironmentValue("DISABLE_CACHE")]
/// public class CachingRepository : IRepository { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class IfNotEnvironmentValueAttribute : Attribute {
    /// <summary>
    /// Registers the service except when the environment has any value for
    /// <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key whose presence skips the registration.</param>
    public IfNotEnvironmentValueAttribute(string key) {
        Key = key;
    }

    /// <summary>
    /// Registers the service except when the environment's value for <paramref name="key"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <param name="value">The value that skips the registration.</param>
    public IfNotEnvironmentValueAttribute(string key, string value) {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// The environment key this registration is gated on.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The value that skips the registration, or null when presence alone skips it.
    /// </summary>
    public string? Value { get; }
}

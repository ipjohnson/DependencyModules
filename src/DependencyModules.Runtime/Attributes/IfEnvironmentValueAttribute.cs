namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Registers the service only when the environment carries a given value.
/// </summary>
/// <remarks>
/// <para>
/// With one argument the key only has to be present; with two it has to equal the given value.
/// Values are compared exactly, unlike environment names, because a value is data rather than a
/// well-known label.
/// </para>
/// <para>
/// This one is <c>AllowMultiple</c>: several keys on one service combine with <b>and</b>, which is
/// how a registration gated on more than one switch is written.
/// </para>
/// <example>
/// <code>
/// [SingletonService]
/// [IfEnvironmentValue("FEATURE_BILLING", "on")]
/// public class BillingService : IBillingService { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class IfEnvironmentValueAttribute : Attribute {
    /// <summary>
    /// Registers the service only when the environment has any value for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key that has to be present.</param>
    public IfEnvironmentValueAttribute(string key) {
        Key = key;
    }

    /// <summary>
    /// Registers the service only when the environment's value for <paramref name="key"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <param name="value">The value it has to equal.</param>
    public IfEnvironmentValueAttribute(string key, string value) {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// The environment key this registration is gated on.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The value the key has to equal, or null when presence alone is enough.
    /// </summary>
    public string? Value { get; }
}

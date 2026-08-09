using System.ComponentModel;
using DependencyModules.Runtime.Interfaces;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// The tests generated code performs for <c>[IfEnvironment]</c> and <c>[IfEnvironmentValue]</c>.
/// </summary>
/// <remarks>
/// The comparison rules live here rather than in emitted text so that they are testable in one
/// place and so that fixing one does not require every consumer to rebuild against a new generator.
/// The negative forms of the attributes emit a <c>!</c> around these rather than adding methods.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EnvironmentConditions {

    /// <summary>
    /// True when the environment name matches any of <paramref name="names"/>.
    /// </summary>
    /// <remarks>
    /// Compared with <see cref="StringComparison.OrdinalIgnoreCase"/>, matching
    /// <c>IHostEnvironment.IsDevelopment()</c>. An environment name is a well-known label rather
    /// than data, and "development" should not be a different environment from "Development".
    /// </remarks>
    /// <param name="environment">The environment to test.</param>
    /// <param name="names">The names to accept.</param>
    /// <returns>True when any name matches.</returns>
    public static bool NameIs(IModuleEnvironment environment, params string[] names) {
        if (environment == null) {
            return false;
        }

        for (var i = 0; i < names.Length; i++) {
            if (string.Equals(environment.EnvironmentName, names[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the environment has any value for <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// A key set to an empty string counts as set. Setting a variable to nothing is a deliberate
    /// act, and treating it as unset would make the two indistinguishable.
    /// </remarks>
    /// <param name="environment">The environment to test.</param>
    /// <param name="key">The key to look for.</param>
    /// <returns>True when a value is present.</returns>
    public static bool HasValue(IModuleEnvironment environment, string key) =>
        environment?.Value(key) != null;

    /// <summary>
    /// True when the environment's value for <paramref name="key"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Compared with <see cref="StringComparison.Ordinal"/>, unlike the environment name. Values
    /// are data, and a comparison that quietly ignored case would be wrong as often as it was
    /// convenient.
    /// </remarks>
    /// <param name="environment">The environment to test.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>True when the value matches exactly.</returns>
    public static bool ValueIs(IModuleEnvironment environment, string key, string value) =>
        string.Equals(environment?.Value(key), value, StringComparison.Ordinal);
}

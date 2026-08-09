namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// What an environment condition tests.
/// </summary>
public enum EnvironmentConditionKind {
    /// <summary>
    /// The environment's name, against one or more accepted names.
    /// </summary>
    Name,

    /// <summary>
    /// A value the environment carries, either for presence or against an exact value.
    /// </summary>
    Value,
}

/// <summary>
/// One <c>[IfEnvironment]</c>-family attribute, read from source.
/// </summary>
/// <remarks>
/// <para>
/// Conditions on one service combine with <b>and</b>; alternatives live inside a single condition's
/// <see cref="Values"/>, which is why <c>IfEnvironment</c> takes <c>params</c> and is not
/// <c>AllowMultiple</c>. There is deliberately no way to write a boolean expression: a registration
/// whose condition cannot be read off the declaration is one nobody can predict from the source.
/// </para>
/// <para>
/// Strings rather than symbols, so the model stays equatable and the incremental cache holds.
/// </para>
/// </remarks>
/// <param name="Kind">What is being tested.</param>
/// <param name="Negate">True for the <c>IfNot</c> forms, which emit the same test behind a <c>!</c>.</param>
/// <param name="Key">The environment key for <see cref="EnvironmentConditionKind.Value"/>; null otherwise.</param>
/// <param name="Values">
/// Accepted names for <see cref="EnvironmentConditionKind.Name"/>. For
/// <see cref="EnvironmentConditionKind.Value"/> this holds the single value to compare against, or
/// is empty when presence of the key is enough.
/// </param>
public record EnvironmentConditionModel(
    EnvironmentConditionKind Kind,
    bool Negate,
    string? Key,
    IReadOnlyList<string> Values) {

    // Structural equality over Values; see ModelEquality.
    public virtual bool Equals(EnvironmentConditionModel? other) =>
        other is not null &&
        Kind == other.Kind &&
        Negate == other.Negate &&
        Key == other.Key &&
        ModelEquality.ListEquals(Values, other.Values);

    public override int GetHashCode() {
        unchecked {
            var hash = (int)Kind;
            hash = hash * 31 + Negate.GetHashCode();
            hash = hash * 31 + (Key?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(Values);
            return hash;
        }
    }
}

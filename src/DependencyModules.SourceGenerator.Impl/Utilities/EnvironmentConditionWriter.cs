using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Renders a set of environment conditions as the guard written into generated code.
/// </summary>
/// <remarks>
/// Shared by every writer that emits something conditional — service registrations and decorators —
/// so the two cannot drift into testing the same attributes differently.
/// </remarks>
public static class EnvironmentConditionWriter {

    private const string ConditionsType =
        "global::" + KnownTypes.DependencyModules.Helpers.Namespace + ".EnvironmentConditions";

    /// <summary>
    /// The guard for one declaration's conditions, as it is written into the generated method.
    /// </summary>
    /// <remarks>
    /// Composed as text rather than through CSharpAuthor because the library has no combinators for
    /// <c>&amp;&amp;</c> or <c>!</c>, and nesting an if per condition to avoid them would emit a
    /// staircase for something that reads as one line. The calls themselves are static, so the
    /// generated file needs no using.
    /// </remarks>
    /// <param name="conditions">Conditions to test, combined with <b>and</b>.</param>
    /// <param name="environmentParameter">Name of the <c>IModuleEnvironment</c> parameter in scope.</param>
    public static string BuildCondition(
        IReadOnlyList<EnvironmentConditionModel> conditions, string environmentParameter) {

        var parts = new List<string>(conditions.Count);

        foreach (var condition in conditions) {
            // An empty condition tests nothing; it is reported as DM0012 and left out rather than
            // emitted as a call that is constant either way.
            if (EnvironmentConditionUtility.IsEmpty(condition)) {
                continue;
            }

            var call = condition.Kind == EnvironmentConditionKind.Name
                ? $"{ConditionsType}.NameIs({environmentParameter}, {QuoteAll(condition.Values)})"
                : condition.Values.Count > 0
                    ? $"{ConditionsType}.ValueIs({environmentParameter}, {QuoteString(condition.Key!)}, {QuoteString(condition.Values[0])})"
                    : $"{ConditionsType}.HasValue({environmentParameter}, {QuoteString(condition.Key!)})";

            parts.Add(condition.Negate ? "!" + call : call);
        }

        // Every condition was empty, so there is nothing left to test and the declaration is
        // unconditional. The diagnostic has already said so.
        return parts.Count == 0 ? "true" : string.Join(" && ", parts);
    }

    private static string QuoteAll(IReadOnlyList<string> values) =>
        string.Join(", ", values.Select(QuoteString));
}

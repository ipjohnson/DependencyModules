using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads the <c>[IfEnvironment]</c> family off a service declaration.
/// </summary>
/// <remarks>
/// <para>
/// The attribute type is resolved through the semantic model rather than matched on the name as
/// written. Matching written names is how a namespace-qualified usage came to be silently ignored
/// once already in this generator, and a condition that is silently ignored registers a service the
/// developer asked to keep out of production.
/// </para>
/// <para>
/// Only the resulting strings reach the model. Symbols are not equatable, and holding one breaks
/// the incremental cache.
/// </para>
/// </remarks>
public static class EnvironmentConditionUtility {

    private const string IfEnvironment = "IfEnvironmentAttribute";
    private const string IfNotEnvironment = "IfNotEnvironmentAttribute";
    private const string IfEnvironmentValue = "IfEnvironmentValueAttribute";
    private const string IfNotEnvironmentValue = "IfNotEnvironmentValueAttribute";

    /// <summary>
    /// Reads every environment condition declared on a service.
    /// </summary>
    /// <returns>
    /// Null when the service carries none, so that an unconditional registration keeps the model it
    /// has always had and existing snapshots do not move.
    /// </returns>
    public static IReadOnlyList<EnvironmentConditionModel>? GetConditions(
        SyntaxTransformContext context, SyntaxNode node, CancellationToken cancellationToken) {

        if (node is not MemberDeclarationSyntax memberDeclaration) {
            return null;
        }

        List<EnvironmentConditionModel>? conditions = null;

        foreach (var attributeList in memberDeclaration.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var condition = ReadCondition(context, attribute);

                if (condition == null) {
                    continue;
                }

                conditions ??= new List<EnvironmentConditionModel>();
                conditions.Add(condition);
            }
        }

        return conditions;
    }

    private static EnvironmentConditionModel? ReadCondition(
        SyntaxTransformContext context, AttributeSyntax attribute) {

        if (ModelExtensions.GetTypeInfo(context.SemanticModel, attribute).Type is not { } attributeType ||
            attributeType.ContainingNamespace.GetFullName() != KnownTypes.DependencyModules.Attributes.Namespace) {
            return null;
        }

        var kind = attributeType.Name switch {
            IfEnvironment or IfNotEnvironment => EnvironmentConditionKind.Name,
            IfEnvironmentValue or IfNotEnvironmentValue => EnvironmentConditionKind.Value,
            _ => (EnvironmentConditionKind?)null,
        };

        if (kind == null) {
            return null;
        }

        var negate = attributeType.Name is IfNotEnvironment or IfNotEnvironmentValue;
        var arguments = ReadStringArguments(context, attribute);

        if (kind == EnvironmentConditionKind.Name) {
            return new EnvironmentConditionModel(
                EnvironmentConditionKind.Name, negate, null, arguments);
        }

        // The key is the first argument; a second, when present, is the value it has to equal.
        // Nothing at all is left as an empty key so the caller can report it rather than emit a
        // test that can never pass.
        var key = arguments.Count > 0 ? arguments[0] : "";
        var values = arguments.Count > 1 ? new[] { arguments[1] } : Array.Empty<string>();

        return new EnvironmentConditionModel(
            EnvironmentConditionKind.Value, negate, key, values);
    }

    /// <summary>
    /// The positional arguments, as constants. Named arguments are skipped — none of these
    /// attributes has a settable property, so one can only be a mistake.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArguments(
        SyntaxTransformContext context, AttributeSyntax attribute) {

        if (attribute.ArgumentList == null) {
            return Array.Empty<string>();
        }

        var values = new List<string>();

        foreach (var argument in attribute.ArgumentList.Arguments) {
            if (argument.NameEquals != null) {
                continue;
            }

            // GetConstantValue rather than the literal text, so nameof(...) and a const declared
            // elsewhere both read as the string they evaluate to.
            if (context.SemanticModel.GetConstantValue(argument.Expression).Value is string value) {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// True when a condition names nothing to test, so its result never depends on the environment.
    /// </summary>
    /// <remarks>
    /// <c>[IfEnvironment()]</c> and <c>[IfEnvironmentValue("")]</c> both compile. Written plain they
    /// mean the service never registers; written as the <c>IfNot</c> form they mean the condition
    /// does nothing at all. Either way the developer wrote a condition that does not condition on
    /// anything, so it is reported rather than emitted.
    /// </remarks>
    public static bool IsEmpty(EnvironmentConditionModel condition) =>
        condition.Kind switch {
            EnvironmentConditionKind.Name => condition.Values.Count == 0,
            EnvironmentConditionKind.Value => string.IsNullOrEmpty(condition.Key),
            _ => false,
        };

    /// <summary>
    /// A condition as it would read in a diagnostic message.
    /// </summary>
    public static string Describe(EnvironmentConditionModel condition) {
        var not = condition.Negate ? "not " : "";

        if (condition.Kind == EnvironmentConditionKind.Name) {
            return $"environment is {not}{string.Join(" or ", condition.Values)}";
        }

        return condition.Values.Count > 0
            ? $"'{condition.Key}' is {not}'{condition.Values[0]}'"
            : $"'{condition.Key}' is {not}set";
    }
}

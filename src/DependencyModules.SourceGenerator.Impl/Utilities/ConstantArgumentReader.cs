using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads the value of an argument from the semantic model, not from its source text.
/// </summary>
/// <remarks>
/// A constant declared elsewhere, a digit separator and a fully qualified enum member all read as
/// the value they evaluate to. The generator does not reference the runtime assembly, so an enum
/// argument arrives as its underlying number. Each method returns null for an argument that has no
/// constant value.
/// </remarks>
public static class ConstantArgumentReader
{
    public static int? ReadInt(SyntaxTransformContext context, ExpressionSyntax expression) =>
        context.SemanticModel.GetConstantValue(expression).Value is int value ? value : null;

    public static bool? ReadBool(SyntaxTransformContext context, ExpressionSyntax expression) =>
        context.SemanticModel.GetConstantValue(expression).Value is bool value ? value : null;

    public static string? ReadString(SyntaxTransformContext context, ExpressionSyntax expression) =>
        context.SemanticModel.GetConstantValue(expression).Value as string;

    /// <remarks>
    /// The generator's <see cref="RegistrationType"/> has the member order of the runtime's.
    /// </remarks>
    public static RegistrationType? ReadRegistrationType(
        SyntaxTransformContext context,
        ExpressionSyntax expression
    ) =>
        ReadInt(context, expression) is { } value && Enum.IsDefined(typeof(RegistrationType), value)
            ? (RegistrationType)value
            : null;

    /// <remarks>
    /// <c>ServiceLifetime</c> numbers its members Singleton, Scoped, Transient, which is the reverse
    /// of <see cref="ServiceLifestyle"/>.
    /// </remarks>
    public static ServiceLifestyle? ReadLifetime(
        SyntaxTransformContext context,
        ExpressionSyntax expression
    ) =>
        ReadInt(context, expression) switch
        {
            0 => ServiceLifestyle.Singleton,
            1 => ServiceLifestyle.Scoped,
            2 => ServiceLifestyle.Transient,
            _ => null,
        };

    /// <remarks>
    /// <see cref="InterceptedMemberKinds"/> has the flag values of the runtime's
    /// <c>InterceptedMembers</c>.
    /// </remarks>
    public static InterceptedMemberKinds? ReadMemberKinds(
        SyntaxTransformContext context,
        ExpressionSyntax expression
    ) =>
        ReadInt(context, expression) is { } value
            ? (InterceptedMemberKinds)value & InterceptedMemberKinds.All
            : null;
}

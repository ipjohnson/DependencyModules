using System.Text;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Renders the members of an interface into the declarations a generated wrapper must implement.
/// </summary>
/// <remarks>
/// Signatures come from Roslyn's own <see cref="SymbolDisplayFormat"/> rather than being assembled
/// by hand. It is the authority on its own syntax, so generics, constraints, ref kinds, default
/// values, params, tuple element names and nullability all render correctly without this file
/// knowing about any of them.
/// </remarks>
public static class InterceptedMemberReader {

    /// <summary>
    /// Renders a member declaration: accessibility is omitted because the wrapper implements the
    /// interface implicitly, and everything else is included so the signature matches exactly.
    /// </summary>
    private static readonly SymbolDisplayFormat DeclarationFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters |
                         SymbolDisplayGenericsOptions.IncludeTypeConstraints |
                         SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeType |
                       SymbolDisplayMemberOptions.IncludeRef |
                       SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeDefaultValue |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                              SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The members a wrapper for <paramref name="serviceType"/> has to implement, including those
    /// inherited from base interfaces.
    /// </summary>
    /// <returns>
    /// Null when the interface contains something that cannot be forwarded, so the caller reports it
    /// rather than emitting a wrapper that does not compile.
    /// </returns>
    public static IReadOnlyList<InterceptedMemberModel>? Read(INamedTypeSymbol serviceType, out string? unsupported) {
        unsupported = null;

        var members = new List<InterceptedMemberModel>();

        foreach (var candidate in EnumerateMembers(serviceType)) {
            if (candidate is not IMethodSymbol method) {
                // Properties, indexers and events surface as their accessor methods, which are
                // handled below; anything else is not something a wrapper can forward.
                continue;
            }

            if (method.IsStatic) {
                unsupported = $"'{method.Name}' is static, which cannot be forwarded through an instance";
                return null;
            }

            if (method.ReturnsByRef || method.ReturnsByRefReadonly) {
                unsupported = $"'{method.Name}' returns by reference, which cannot be wrapped";
                return null;
            }

            if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or
                MethodKind.EventAdd or MethodKind.EventRemove) {
                unsupported = $"'{serviceType.Name}' declares properties or events, which are not supported yet";
                return null;
            }

            if (method.MethodKind != MethodKind.Ordinary) {
                continue;
            }

            members.Add(new InterceptedMemberModel(
                method.ToDisplayString(DeclarationFormat),
                method.Name,
                RenderArguments(method),
                GetReturnShape(method.ReturnType)));
        }

        return members;
    }

    /// <summary>
    /// The interface's own members plus everything it inherits. Skipping base interfaces would
    /// produce a wrapper that does not satisfy the interface.
    /// </summary>
    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol serviceType) {
        foreach (var member in serviceType.GetMembers()) {
            yield return member;
        }

        foreach (var baseInterface in serviceType.AllInterfaces) {
            foreach (var member in baseInterface.GetMembers()) {
                yield return member;
            }
        }
    }

    /// <summary>
    /// The argument list for forwarding, repeating any ref or out modifier at the call site.
    /// </summary>
    private static string RenderArguments(IMethodSymbol method) {
        var builder = new StringBuilder();

        for (var i = 0; i < method.Parameters.Length; i++) {
            if (i > 0) {
                builder.Append(", ");
            }

            var parameter = method.Parameters[i];

            builder.Append(parameter.RefKind switch {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => ""
            });

            builder.Append(EscapeIdentifier(parameter.Name));
        }

        return builder.ToString();
    }

    private static string EscapeIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? name
            : "@" + name;

    private static ReturnShape GetReturnShape(ITypeSymbol returnType) {
        if (returnType.SpecialType == SpecialType.System_Void) {
            return ReturnShape.Void;
        }

        var name = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (name.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal)) {
            return ReturnShape.TaskOfValue;
        }

        if (name == "global::System.Threading.Tasks.Task") {
            return ReturnShape.Task;
        }

        if (name.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal)) {
            return ReturnShape.ValueTaskOfValue;
        }

        if (name == "global::System.Threading.Tasks.ValueTask") {
            return ReturnShape.ValueTask;
        }

        return ReturnShape.Value;
    }
}

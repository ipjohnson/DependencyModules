using CSharpAuthor;
using DependencyModules.Conventions.Models;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.Conventions.Utilities;

/// <summary>
/// Reads the conventions a module declares out of its <c>Conventions</c> method body.
/// </summary>
/// <remarks>
/// <para>
/// The body is configuration that happens to be type-checked C#: it is never executed, so it is read
/// rather than run. That makes the set of things it may contain small and closed, and everything
/// outside that set has to be refused. A statement this cannot read becomes an
/// <see cref="UnreadableStatementModel"/> and is reported as DM0009 — never dropped. A convention
/// registration that silently fails to appear is the single failure mode this codebase has spent the
/// most time hunting down.
/// </para>
/// <para>
/// Nothing here touches a symbol beyond resolving a service type to an <see cref="ITypeDefinition"/>,
/// because the result lands in an incremental model. Symbols are not equatable and holding one
/// breaks the cache.
/// </para>
/// </remarks>
public static class ConventionModelUtility {

    private const string RegisterAll = "RegisterAll";
    private const string IncludeBaseClasses = "IncludeBaseClasses";

    private static readonly Dictionary<string, ServiceLifestyle> LifetimeCalls = new() {
        ["AsSingleton"] = ServiceLifestyle.Singleton,
        ["AsScoped"] = ServiceLifestyle.Scoped,
        ["AsTransient"] = ServiceLifestyle.Transient,
    };

    /// <summary>
    /// Cheap syntactic test for the provider predicate: does this declaration name
    /// <c>IConventionModule</c> in its base list.
    /// </summary>
    /// <remarks>
    /// Deliberately a name comparison. The predicate runs on a great many nodes and must not touch
    /// the semantic model; the transform confirms it is the right <c>IConventionModule</c> before
    /// reading anything.
    /// </remarks>
    public static bool IsConventionModuleCandidate(SyntaxNode node, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (node is not TypeDeclarationSyntax { BaseList: not null } typeDeclaration) {
            return false;
        }

        foreach (var baseType in typeDeclaration.BaseList.Types) {
            if (SimpleNameOf(baseType.Type) == ConventionContractSource.ConventionModule) {
                return true;
            }
        }

        return false;
    }

    public static ConventionModuleModel GetConventionModuleModel(
        SyntaxTransformContext context, CancellationToken cancellationToken) {

        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclaration) {
            return ConventionModuleModel.Ignore;
        }

        if (!ImplementsConventionModule(context, typeDeclaration)) {
            return ConventionModuleModel.Ignore;
        }

        var method = FindConventionsMethod(typeDeclaration);

        if (method?.Body == null) {
            // An expression-bodied or abstract declaration has nothing to read. Reported rather than
            // ignored: the module said it had conventions and produced none.
            return new ConventionModuleModel(
                typeDeclaration.GetTypeDefinition(),
                Array.Empty<ConventionModel>(),
                new[] {
                    new UnreadableStatementModel(
                        method?.ToString() ?? ConventionContractSource.ConventionMethod,
                        "the Conventions method needs a statement body containing RegisterAll calls",
                        LocationModel.From((SyntaxNode?)method ?? typeDeclaration))
                });
        }

        var parameterName = method.ParameterList.Parameters.FirstOrDefault()?.Identifier.Text;

        if (string.IsNullOrEmpty(parameterName)) {
            return ConventionModuleModel.Ignore;
        }

        var conventions = new List<ConventionModel>();
        var unreadable = new List<UnreadableStatementModel>();

        foreach (var statement in method.Body.Statements) {
            cancellationToken.ThrowIfCancellationRequested();

            ReadStatement(context, statement, parameterName!, conventions, unreadable);
        }

        return new ConventionModuleModel(typeDeclaration.GetTypeDefinition(), conventions, unreadable);
    }

    private static void ReadStatement(
        SyntaxTransformContext context,
        StatementSyntax statement,
        string parameterName,
        List<ConventionModel> conventions,
        List<UnreadableStatementModel> unreadable) {

        if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation }) {
            unreadable.Add(Refuse(
                statement,
                "only RegisterAll chains can appear here, because this body is read at compile time " +
                "rather than executed"));

            return;
        }

        var chain = UnwrapChain(invocation, parameterName);

        if (chain == null) {
            unreadable.Add(Refuse(
                statement,
                $"expected a chain of calls on '{parameterName}'"));

            return;
        }

        var convention = BuildConvention(context, statement, chain, out var reason);

        if (convention == null) {
            unreadable.Add(Refuse(statement, reason!));

            return;
        }

        conventions.Add(convention);
    }

    /// <summary>
    /// Flattens <c>conventions.RegisterAll&lt;T&gt;().AsScoped()</c> into the calls in source order.
    /// </summary>
    /// <remarks>
    /// The chain nests outward-in — the last call written is the outermost node — so it is unwrapped
    /// from the outside and reversed. Returns null the moment anything other than an invocation on a
    /// member access appears, or the chain does not bottom out at the parameter, which is what stops
    /// a helper call or an unrelated statement being read as a convention.
    /// </remarks>
    private static List<InvocationExpressionSyntax>? UnwrapChain(
        InvocationExpressionSyntax invocation, string parameterName) {

        var calls = new List<InvocationExpressionSyntax>();
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax candidate) {
            if (candidate.Expression is not MemberAccessExpressionSyntax access) {
                return null;
            }

            calls.Add(candidate);
            current = access.Expression;
        }

        if (current is not IdentifierNameSyntax identifier ||
            identifier.Identifier.Text != parameterName) {
            return null;
        }

        calls.Reverse();

        return calls;
    }

    private static ConventionModel? BuildConvention(
        SyntaxTransformContext context,
        StatementSyntax statement,
        List<InvocationExpressionSyntax> chain,
        out string? reason) {

        reason = null;

        var head = chain[0];
        var headName = MethodNameOf(head);

        if (headName != RegisterAll) {
            reason = $"a convention has to start with {RegisterAll}, not '{headName}'";

            return null;
        }

        var serviceType = ReadServiceType(context, head, out var isOpenGeneric);

        if (serviceType == null) {
            reason =
                $"could not resolve the service type; write {RegisterAll}<IService>() or " +
                $"{RegisterAll}(typeof(IService<>))";

            return null;
        }

        ServiceLifestyle? lifestyle = null;
        var includeBaseClasses = false;

        for (var i = 1; i < chain.Count; i++) {
            var name = MethodNameOf(chain[i]);

            if (LifetimeCalls.TryGetValue(name, out var candidateLifestyle)) {
                if (lifestyle != null) {
                    reason = "a convention declares one lifetime, and this one declares more than one";

                    return null;
                }

                lifestyle = candidateLifestyle;

                continue;
            }

            if (name == IncludeBaseClasses) {
                includeBaseClasses = true;

                continue;
            }

            reason = $"'{name}' is not a convention call";

            return null;
        }

        // Left null on purpose. A lifetime nobody wrote down is the most expensive thing for a
        // registration to get wrong, so it is reported at output rather than defaulted here.
        return new ConventionModel(
            serviceType,
            ConventionTypeKey.For(serviceType),
            isOpenGeneric,
            lifestyle,
            includeBaseClasses,
            LocationModel.From(statement));
    }

    /// <summary>
    /// Resolves the service type from either <c>RegisterAll&lt;T&gt;()</c> or
    /// <c>RegisterAll(typeof(T))</c>.
    /// </summary>
    private static ITypeDefinition? ReadServiceType(
        SyntaxTransformContext context, InvocationExpressionSyntax invocation, out bool isOpenGeneric) {

        isOpenGeneric = false;

        if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic }) {
            return generic.TypeArgumentList.Arguments[0].GetTypeDefinition(context);
        }

        var argument = invocation.ArgumentList.Arguments.Count == 1
            ? invocation.ArgumentList.Arguments[0].Expression
            : null;

        if (argument is not TypeOfExpressionSyntax typeOf) {
            return null;
        }

        isOpenGeneric = IsUnboundGeneric(typeOf.Type);

        return typeOf.Type.GetTypeDefinition(context);
    }

    /// <summary>
    /// True for <c>typeof(IHandler&lt;,&gt;)</c>, where the type arguments are omitted.
    /// </summary>
    private static bool IsUnboundGeneric(TypeSyntax type) {
        var generic = type as GenericNameSyntax ??
                      (type as QualifiedNameSyntax)?.Right as GenericNameSyntax;

        return generic != null &&
               generic.TypeArgumentList.Arguments.Any(argument => argument is OmittedTypeArgumentSyntax);
    }

    private static UnreadableStatementModel Refuse(StatementSyntax statement, string reason) =>
        new(Summarise(statement), reason, LocationModel.From(statement));

    /// <summary>
    /// A single line of the refused statement, so the diagnostic message stays readable when what
    /// was refused is a loop or a block.
    /// </summary>
    private static string Summarise(StatementSyntax statement) {
        var text = statement.ToString().Replace("\r", " ").Replace("\n", " ").Trim();

        while (text.Contains("  ")) {
            text = text.Replace("  ", " ");
        }

        return text.Length <= 80 ? text : text.Substring(0, 77) + "...";
    }

    private static MethodDeclarationSyntax? FindConventionsMethod(TypeDeclarationSyntax typeDeclaration) {
        MethodDeclarationSyntax? fallback = null;

        foreach (var method in typeDeclaration.Members.OfType<MethodDeclarationSyntax>()) {
            if (method.Identifier.Text != ConventionContractSource.ConventionMethod ||
                method.ParameterList.Parameters.Count != 1) {
                continue;
            }

            // The explicit implementation is the shape that compiles: an ordinary public one has a
            // parameter of an internal type, which is CS0051. Preferred, but an implicit one is
            // still read so the diagnostic comes from this generator rather than only from the
            // compiler.
            if (method.ExplicitInterfaceSpecifier != null) {
                return method;
            }

            fallback ??= method;
        }

        return fallback;
    }

    private static bool ImplementsConventionModule(
        SyntaxTransformContext context, TypeDeclarationSyntax typeDeclaration) {

        if (typeDeclaration.BaseList == null) {
            return false;
        }

        foreach (var baseType in typeDeclaration.BaseList.Types) {
            if (SimpleNameOf(baseType.Type) != ConventionContractSource.ConventionModule) {
                continue;
            }

            // Confirms it is the emitted contract rather than a same-named interface of the
            // developer's own, which the syntactic predicate cannot tell apart.
            if (context.SemanticModel.GetSymbolInfo(baseType.Type).Symbol is INamedTypeSymbol symbol &&
                symbol.ContainingNamespace.GetFullName() == ConventionContractSource.Namespace) {
                return true;
            }
        }

        return false;
    }

    private static string MethodNameOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax access
            ? access.Name.Identifier.Text
            : "";

    /// <summary>
    /// The unqualified name of a written type, without namespace or type arguments.
    /// </summary>
    private static string SimpleNameOf(TypeSyntax type) =>
        type switch {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => type.ToString(),
        };
}

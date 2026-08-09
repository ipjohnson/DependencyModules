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
    private const string UsingCall = "Using";
    private const string WithAttributeCall = "WithAttribute";
    private const string WithoutAttributeCall = "WithoutAttribute";
    private const string WithNameCall = "WithName";
    private const string WithoutNameCall = "WithoutName";
    private const string AsCall = "As";
    private const string AsMatchingInterfaceCall = "AsMatchingInterface";
    private const string InAssemblyOfCall = "InAssemblyOf";
    private const string WithKeyCall = "WithKey";

    private static readonly Dictionary<string, ServiceLifestyle> LifetimeCalls = new() {
        ["AsSingleton"] = ServiceLifestyle.Singleton,
        ["AsScoped"] = ServiceLifestyle.Scoped,
        ["AsTransient"] = ServiceLifestyle.Transient,
    };

    private static readonly Dictionary<string, ConventionRegisterAs> RegisterAsCalls = new() {
        ["AsSelf"] = ConventionRegisterAs.Self,
        ["AsSelfWithInterfaces"] = ConventionRegisterAs.SelfAndInterfaces,
        ["AlsoAsSelf"] = ConventionRegisterAs.AlsoSelf,
    };

    /// <summary>
    /// The namespace filter calls, and the shape of filter each produces.
    /// </summary>
    private static readonly Dictionary<string, (bool Exact, bool Exclude)> NamespaceCalls = new() {
        ["InNamespaceOf"] = (false, false),
        ["InNamespaces"] = (false, false),
        ["InExactNamespaces"] = (true, false),
        ["NotInNamespaceOf"] = (false, true),
        ["NotInNamespaces"] = (false, true),
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

        // No type argument and no argument at all is the filter-selected form, which is valid and
        // has no service type. Anything else that fails to resolve is a mistake.
        var selectsByFilter =
            head.Expression is MemberAccessExpressionSyntax { Name: not GenericNameSyntax } &&
            head.ArgumentList.Arguments.Count == 0;

        ITypeDefinition? serviceType = null;
        var isOpenGeneric = false;

        if (!selectsByFilter) {
            serviceType = ReadServiceType(context, head, out isOpenGeneric);

            if (serviceType == null) {
                reason =
                    $"could not resolve the service type; write {RegisterAll}<IService>(), " +
                    $"{RegisterAll}(typeof(IService<>)) or {RegisterAll}() with a filter";

                return null;
            }
        }

        ServiceLifestyle? lifestyle = null;
        var includeBaseClasses = false;
        var registerAs = ConventionRegisterAs.Interfaces;
        List<NamespaceFilterModel>? namespaceFilters = null;
        RegistrationType? registrationType = null;
        object? key = null;
        IReadOnlyList<string>? keyNamespaces = null;
        List<AttributeFilterModel>? attributeFilters = null;
        List<NameFilterModel>? nameFilters = null;
        ITypeDefinition? explicitServiceType = null;
        string? assemblyName = null;

        for (var i = 1; i < chain.Count; i++) {
            var call = chain[i];
            var name = MethodNameOf(call);

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

            if (RegisterAsCalls.TryGetValue(name, out var candidateRegisterAs)) {
                if (registerAs != ConventionRegisterAs.Interfaces && registerAs != candidateRegisterAs) {
                    reason = "a convention registers matches one way, and this one says more than one";

                    return null;
                }

                registerAs = candidateRegisterAs;

                continue;
            }

            if (name == UsingCall) {
                var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;

                registrationType = argument == null
                    ? null
                    : SourceGenerator.Impl.BaseSourceGenerator.GetRegistrationType(argument.ToString());

                if (registrationType == null) {
                    reason = $"'{name}' needs a RegistrationType it can read at compile time";

                    return null;
                }

                continue;
            }

            if (name == WithKeyCall) {
                var argument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;

                if (argument == null) {
                    reason = $"'{name}' needs a key";

                    return null;
                }

                // Kept as it was written, the way the attribute path keeps it, so a literal, a const
                // and an enum member all reach the emitted registration unchanged.
                key = argument.ToString();

                if (argument is MemberAccessExpressionSyntax memberAccess) {
                    keyNamespaces = memberAccess.GetTypeDefinition(context)?.KnownNamespaces.ToArray();
                }

                continue;
            }

            if (name == InAssemblyOfCall) {
                assemblyName = MarkerAssemblyNameOf(context, call);

                if (assemblyName == null) {
                    reason = $"'{name}' needs a type argument from the assembly to scan";

                    return null;
                }

                continue;
            }

            if (name == AsMatchingInterfaceCall) {
                registerAs = ConventionRegisterAs.MatchingInterface;

                continue;
            }

            if (name == AsCall) {
                explicitServiceType = SingleTypeArgumentOf(context, call);

                if (explicitServiceType == null) {
                    reason = $"'{name}' needs a service type argument";

                    return null;
                }

                registerAs = ConventionRegisterAs.Explicit;

                continue;
            }

            if (name is WithNameCall or WithoutNameCall) {
                var patterns = ReadPatterns(context, call);

                if (patterns.Count == 0) {
                    reason = $"'{name}' needs at least one pattern it can read at compile time";

                    return null;
                }

                nameFilters ??= new List<NameFilterModel>();

                foreach (var pattern in patterns) {
                    nameFilters.Add(new NameFilterModel(pattern, name == WithoutNameCall));
                }

                continue;
            }

            if (name is WithAttributeCall or WithoutAttributeCall) {
                var attributeType = SingleTypeArgumentOf(context, call);

                if (attributeType == null) {
                    reason = $"'{name}' needs an attribute type argument";

                    return null;
                }

                attributeFilters ??= new List<AttributeFilterModel>();
                attributeFilters.Add(new AttributeFilterModel(
                    ConventionTypeKey.For(attributeType), name == WithoutAttributeCall));

                continue;
            }

            if (NamespaceCalls.TryGetValue(name, out var namespaceCall)) {
                var read = ReadNamespaceFilters(context, call, namespaceCall);

                if (read == null) {
                    reason = $"'{name}' needs a namespace it can read at compile time";

                    return null;
                }

                namespaceFilters ??= new List<NamespaceFilterModel>();
                namespaceFilters.AddRange(read);

                continue;
            }

            reason = $"'{name}' is not a convention call";

            return null;
        }

        if (selectsByFilter) {
            if (registerAs == ConventionRegisterAs.Interfaces) {
                reason =
                    $"{RegisterAll}() names no service type, so there is nothing to register the " +
                    "matches as; call AsSelf() or AsSelfWithInterfaces()";

                return null;
            }

            var hasInclusion =
                namespaceFilters?.Any(filter => !filter.Exclude) == true ||
                nameFilters?.Any(filter => !filter.Exclude) == true ||
                attributeFilters?.Any(filter => !filter.Exclude) == true;

            if (!hasInclusion) {
                reason =
                    $"{RegisterAll}() with no filter matches every class in the compilation; " +
                    "narrow it with InNamespaceOf<T>() or InNamespaces(...)";

                return null;
            }
        }

        // Lifestyle left null on purpose. A lifetime nobody wrote down is the most expensive thing
        // for a registration to get wrong, so it is reported at output rather than defaulted here.
        return new ConventionModel(
            serviceType,
            serviceType == null ? null : ConventionTypeKey.For(serviceType),
            isOpenGeneric,
            lifestyle,
            includeBaseClasses,
            LocationModel.From(statement),
            registerAs,
            namespaceFilters,
            registrationType,
            key,
            keyNamespaces,
            attributeFilters,
            nameFilters,
            explicitServiceType,
            assemblyName);
    }

    /// <summary>
    /// The constant string arguments of a filter call.
    /// </summary>
    /// <remarks>
    /// <c>GetConstantValue</c> rather than the literal text, so a <c>const</c> declared elsewhere
    /// reads as the string it evaluates to.
    /// </remarks>
    private static IReadOnlyList<string> ReadPatterns(
        SyntaxTransformContext context, InvocationExpressionSyntax call) {

        var values = new List<string>();

        foreach (var argument in call.ArgumentList.Arguments) {
            if (context.SemanticModel.GetConstantValue(argument.Expression).Value is string value) {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// The name of the assembly the call's marker type lives in.
    /// </summary>
    /// <remarks>
    /// A name rather than a symbol, because this lands in an incremental model and symbols are not
    /// equatable. It is also what lets a reference be rejected before any symbol work happens, which
    /// is what keeps a metadata scan affordable.
    /// </remarks>
    private static string? MarkerAssemblyNameOf(
        SyntaxTransformContext context, InvocationExpressionSyntax call) {

        if (call.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } ||
            generic.TypeArgumentList.Arguments.Count != 1) {
            return null;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(generic.TypeArgumentList.Arguments[0]).Symbol;

        return symbol?.ContainingAssembly?.Name;
    }

    /// <summary>
    /// The single type argument of a generic filter call, resolved.
    /// </summary>
    private static ITypeDefinition? SingleTypeArgumentOf(
        SyntaxTransformContext context, InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } &&
        generic.TypeArgumentList.Arguments.Count == 1
            ? generic.TypeArgumentList.Arguments[0].GetTypeDefinition(context)
            : null;

    /// <summary>
    /// Reads the namespaces one filter call names, from a marker type argument or from string
    /// literals.
    /// </summary>
    private static List<NamespaceFilterModel>? ReadNamespaceFilters(
        SyntaxTransformContext context, InvocationExpressionSyntax call, (bool Exact, bool Exclude) form) {

        var filters = new List<NamespaceFilterModel>();

        // InNamespaceOf<TMarker>() — the namespace is wherever the marker type lives.
        if (call.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } &&
            generic.TypeArgumentList.Arguments.Count == 1) {
            var marker = generic.TypeArgumentList.Arguments[0].GetTypeDefinition(context);

            if (marker == null) {
                return null;
            }

            filters.Add(new NamespaceFilterModel(marker.Namespace ?? "", form.Exact, form.Exclude));

            return filters;
        }

        foreach (var argument in call.ArgumentList.Arguments) {
            if (context.SemanticModel.GetConstantValue(argument.Expression).Value is not string value) {
                return null;
            }

            filters.Add(new NamespaceFilterModel(value, form.Exact, form.Exclude));
        }

        return filters.Count > 0 ? filters : null;
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

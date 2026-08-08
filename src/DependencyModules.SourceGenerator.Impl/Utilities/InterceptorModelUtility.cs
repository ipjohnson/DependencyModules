using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Builds an <see cref="InterceptorModel"/> from a class carrying <c>[Intercept]</c>.
/// </summary>
public static class InterceptorModelUtility {

    private const string InterceptionNamespace = "DependencyModules.Runtime.Interception";

    /// <summary>
    /// Reads the attribute, the interface it intercepts, and the interfaces its interceptors
    /// implement.
    /// </summary>
    /// <returns>
    /// A usable model, <see cref="InterceptorModel.Ignore"/> when the node has nothing to generate
    /// and nothing to report, or a refusal carrying the reason so the output stage can report it.
    /// A diagnostic cannot be raised from here — the transform holds no context that can.
    /// </returns>
    public static InterceptorModel GetInterceptorModel(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {

        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax) {
            return InterceptorModel.Ignore;
        }

        var attribute = FindAttribute(typeDeclarationSyntax);

        if (attribute == null) {
            return InterceptorModel.Ignore;
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclarationSyntax, cancellationToken)
            is not INamedTypeSymbol implementationSymbol) {
            return InterceptorModel.Ignore;
        }

        // An open generic is registered as a definition rather than a constructed type, so there is
        // no instance for a wrapper to be built around.
        if (implementationSymbol.IsGenericType) {
            return InterceptorModel.Refused(
                RefusalKind.CannotIntercept,
                $"'{implementationSymbol.Name}' is generic, and an open generic registration has no " +
                "constructed instance to wrap");
        }

        var interceptorSymbols = new List<INamedTypeSymbol>();
        var order = 0;
        INamedTypeSymbol? explicitService = null;

        ReadAttribute(attribute, context, cancellationToken, interceptorSymbols, ref order, ref explicitService);

        if (interceptorSymbols.Count == 0) {
            return InterceptorModel.Ignore;
        }

        var serviceSymbol = ResolveServiceInterface(implementationSymbol, explicitService, out var unsupported);

        if (serviceSymbol == null) {
            return Refuse(unsupported);
        }

        if (!InterceptedMemberReader.Read(serviceSymbol, out var members, out var declarations, out unsupported)) {
            return Refuse(unsupported);
        }

        if (members.Count == 0) {
            return InterceptorModel.Refused(
                RefusalKind.CannotIntercept,
                $"'{serviceSymbol.Name}' declares nothing to intercept");
        }

        var interceptors = new List<InterceptorTypeModel>();

        foreach (var interceptorSymbol in interceptorSymbols) {
            var interceptor = ReadInterceptorType(interceptorSymbol);

            if (!interceptor.Sync && !interceptor.Async && !interceptor.Stream) {
                return InterceptorModel.Refused(
                    RefusalKind.InterceptorCannotServeMember,
                    $"'{interceptorSymbol.Name}' implements none of IInterceptor, IAsyncInterceptor or " +
                    "IAsyncEnumerableInterceptor, so it cannot intercept anything");
            }

            var uncovered = FindUncoveredMember(interceptor, members);

            if (uncovered != null) {
                return InterceptorModel.Refused(
                    RefusalKind.InterceptorCannotServeMember,
                    Uncovered(interceptorSymbol.Name, uncovered));
            }

            interceptors.Add(interceptor);
        }

        return new InterceptorModel(
            serviceSymbol.GetTypeDefinition(),
            ToTypeDefinition(implementationSymbol),
            interceptors,
            members,
            declarations,
            order);
    }

    private static InterceptorModel Refuse(string? reason) =>
        reason == null
            ? InterceptorModel.Ignore
            : InterceptorModel.Refused(RefusalKind.CannotIntercept, reason);

    /// <summary>
    /// The interfaces an interceptor implements, which decide the members it can be placed around.
    /// </summary>
    private static InterceptorTypeModel ReadInterceptorType(INamedTypeSymbol symbol) {
        var sync = false;
        var async = false;
        var stream = false;

        foreach (var implemented in symbol.AllInterfaces) {
            if (implemented.ContainingNamespace?.ToDisplayString() != InterceptionNamespace) {
                continue;
            }

            switch (implemented.Name) {
                case "IInterceptor":
                    sync = true;
                    break;
                case "IAsyncInterceptor":
                    async = true;
                    break;
                case "IAsyncEnumerableInterceptor":
                    stream = true;
                    break;
            }
        }

        return new InterceptorTypeModel(symbol.GetTypeDefinition(), sync, async, stream);
    }

    /// <summary>
    /// The first member this interceptor has no way to serve. Reporting it is the point: skipping it
    /// silently would leave a call unintercepted with nothing to say so.
    /// </summary>
    private static InterceptedMemberModel? FindUncoveredMember(
        InterceptorTypeModel interceptor, IReadOnlyList<InterceptedMemberModel> members) {

        foreach (var member in members) {
            if (!interceptor.CanServe(member.Kind)) {
                return member;
            }
        }

        return null;
    }

    private static string Uncovered(string interceptorName, InterceptedMemberModel member) {
        var (required, because) = member.Kind switch {
            InterceptorKind.Async => ("IAsyncInterceptor", "returns a task"),
            InterceptorKind.Stream => ("IAsyncEnumerableInterceptor", "returns an async stream"),
            _ => ("IInterceptor", "returns its result directly")
        };

        return $"'{interceptorName}' does not implement {required}, which '{member.Name}' needs " +
               $"because it {because}. Implement {required} on '{interceptorName}', or intercept a " +
               "service without that member";
    }

    private static void ReadAttribute(
        AttributeSyntax attribute,
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken,
        List<INamedTypeSymbol> interceptors,
        ref int order,
        ref INamedTypeSymbol? explicitService) {

        if (attribute.ArgumentList == null) {
            return;
        }

        foreach (var argument in attribute.ArgumentList.Arguments) {
            var name = argument.NameEquals?.Name.ToString();

            if (name == null) {
                if (argument.Expression is TypeOfExpressionSyntax typeOf) {
                    var symbol = ResolveType(typeOf, context, cancellationToken);

                    if (symbol != null) {
                        interceptors.Add(symbol);
                    }
                }

                continue;
            }

            switch (name) {
                case "Order":
                    if (int.TryParse(argument.Expression.ToString(), out var parsed)) {
                        order = parsed;
                    }
                    break;
                case "Service":
                    if (argument.Expression is TypeOfExpressionSyntax serviceTypeOf) {
                        explicitService = ResolveType(serviceTypeOf, context, cancellationToken);
                    }
                    break;
            }
        }
    }

    private static INamedTypeSymbol? ResolveType(
        TypeOfExpressionSyntax typeOf, GeneratorSyntaxContext context, CancellationToken cancellationToken) =>
        context.SemanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type as INamedTypeSymbol;

    /// <summary>
    /// The interface to wrap. Interception works through an interface: a call the implementation
    /// makes to itself never passes through the wrapper, and a class with no interface has nothing
    /// to wrap.
    /// </summary>
    private static INamedTypeSymbol? ResolveServiceInterface(
        INamedTypeSymbol implementation, INamedTypeSymbol? explicitService, out string? unsupported) {

        unsupported = null;

        var interfaces = implementation.Interfaces;

        if (interfaces.Length == 0) {
            unsupported = $"'{implementation.Name}' implements no interface, so there is nothing to intercept";
            return null;
        }

        if (explicitService != null) {
            foreach (var candidate in interfaces) {
                if (SymbolEqualityComparer.Default.Equals(candidate, explicitService)) {
                    return candidate;
                }
            }

            unsupported = $"'{implementation.Name}' does not implement '{explicitService.Name}'";
            return null;
        }

        if (interfaces.Length > 1) {
            unsupported =
                $"'{implementation.Name}' implements more than one interface; set Service to choose which to intercept";
            return null;
        }

        return interfaces[0];
    }

    private static ITypeDefinition ToTypeDefinition(INamedTypeSymbol symbol) {
        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var name = symbol.Name;

        for (var containing = symbol.ContainingType; containing != null; containing = containing.ContainingType) {
            name = containing.Name + "." + name;
        }

        return TypeDefinition.Get(namespaceName, name);
    }

    private static AttributeSyntax? FindAttribute(TypeDeclarationSyntax typeDeclarationSyntax) {
        foreach (var attributeList in typeDeclarationSyntax.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var name = attribute.Name.ToString();

                if (name is "Intercept" or "InterceptAttribute") {
                    return attribute;
                }
            }
        }

        return null;
    }
}

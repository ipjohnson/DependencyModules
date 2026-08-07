using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Builds an <see cref="InterceptorModel"/> from a class carrying <c>[Intercept]</c>.
/// </summary>
public static class InterceptorModelUtility {

    /// <summary>
    /// Reads the attribute and the interface it intercepts. Returns null when the declaration cannot
    /// be intercepted, with <paramref name="unsupported"/> describing why so the caller can report it.
    /// </summary>
    public static InterceptorModel? GetInterceptorModel(
        GeneratorSyntaxContext context, CancellationToken cancellationToken, out string? unsupported) {

        cancellationToken.ThrowIfCancellationRequested();
        unsupported = null;

        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax) {
            return null;
        }

        var attribute = FindAttribute(typeDeclarationSyntax);

        if (attribute == null) {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclarationSyntax, cancellationToken)
            is not INamedTypeSymbol implementationSymbol) {
            return null;
        }

        var interceptors = new List<ITypeDefinition>();
        var order = 0;
        ITypeDefinition? explicitService = null;

        ReadAttribute(attribute, context, interceptors, ref order, ref explicitService);

        if (interceptors.Count == 0) {
            return null;
        }

        var serviceSymbol = ResolveServiceInterface(implementationSymbol, explicitService, out unsupported);

        if (serviceSymbol == null) {
            return null;
        }

        var members = InterceptedMemberReader.Read(serviceSymbol, out unsupported);

        if (members == null) {
            return null;
        }

        if (members.Count == 0) {
            unsupported = $"'{serviceSymbol.Name}' declares no methods to intercept";
            return null;
        }

        return new InterceptorModel(
            ToTypeDefinition(serviceSymbol),
            ToTypeDefinition(implementationSymbol),
            interceptors,
            members,
            order);
    }

    private static void ReadAttribute(
        AttributeSyntax attribute,
        GeneratorSyntaxContext context,
        List<ITypeDefinition> interceptors,
        ref int order,
        ref ITypeDefinition? explicitService) {

        if (attribute.ArgumentList == null) {
            return;
        }

        foreach (var argument in attribute.ArgumentList.Arguments) {
            var name = argument.NameEquals?.Name.ToString();

            if (name == null) {
                if (argument.Expression is TypeOfExpressionSyntax typeOf) {
                    var type = typeOf.Type.GetTypeDefinition(context);

                    if (type != null) {
                        interceptors.Add(type);
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
                        explicitService = serviceTypeOf.Type.GetTypeDefinition(context);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The interface to wrap. Interception works through an interface: a call the implementation
    /// makes to itself never passes through the wrapper, and a class with no interface has nothing
    /// to wrap.
    /// </summary>
    private static INamedTypeSymbol? ResolveServiceInterface(
        INamedTypeSymbol implementation, ITypeDefinition? explicitService, out string? unsupported) {

        unsupported = null;

        var interfaces = implementation.Interfaces;

        if (interfaces.Length == 0) {
            unsupported = $"'{implementation.Name}' implements no interface, so there is nothing to intercept";
            return null;
        }

        if (explicitService != null) {
            foreach (var candidate in interfaces) {
                if (candidate.Name == explicitService.Name) {
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

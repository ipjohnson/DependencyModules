using System.Collections.Immutable;
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
        SyntaxTransformContext context, CancellationToken cancellationToken) {

        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax) {
            return InterceptorModel.Ignore;
        }

        var attributes = FindAttributes(typeDeclarationSyntax);

        if (attributes.Count == 0) {
            return InterceptorModel.Ignore;
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclarationSyntax, cancellationToken)
            is not INamedTypeSymbol implementationSymbol) {
            return InterceptorModel.Ignore;
        }

        // A generic implementation registers as an open generic. Decoration cannot touch one — it
        // rewrites the registration into a factory, and the container refuses a factory for an open
        // generic service type — but interception does not need one: the wrapper is a generated type,
        // and an open generic implementation type is exactly what the container does accept. It is
        // registered as the service, and takes the implementation by its own type so that resolving
        // it does not come back round to the wrapper.
        //
        // Constraints are the one thing that shape cannot carry. The wrapper has to repeat the
        // implementation's constraints to reference it, and the writer has no way to emit them, so a
        // constrained implementation is refused rather than emitted as code that does not compile.
        if (implementationSymbol.IsGenericType) {
            var constrained = ConstrainedTypeParameter(implementationSymbol);

            if (constrained != null) {
                return InterceptorModel.Refused(
                    $"'{implementationSymbol.Name}' is generic and its type parameter " +
                    $"'{constrained}' is constrained. The generated wrapper would have to repeat the " +
                    "constraint and cannot, so it was not generated. Intercept a closed construction " +
                    $"instead, such as a class deriving from '{implementationSymbol.Name}<...>'");
            }
        }

        var interceptorSymbols = new List<INamedTypeSymbol>();
        var order = 0;
        INamedTypeSymbol? explicitService = null;

        // Every [Intercept], not the first. The attribute is AllowMultiple, so stacking them is a
        // supported way to write what one attribute can also express as a params list — and reading
        // only the first dropped every interceptor after it, with nothing to say so.
        foreach (var attribute in attributes) {
            ReadAttribute(attribute, context, cancellationToken, interceptorSymbols, ref order, ref explicitService);
        }

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
                $"'{serviceSymbol.Name}' declares nothing to intercept");
        }

        var interceptors = new List<InterceptorTypeModel>();

        foreach (var interceptorSymbol in interceptorSymbols) {
            interceptors.Add(ReadInterceptorType(interceptorSymbol));
        }

        // Nothing here can be placed around anything, so the wrapper would forward every call
        // untouched, and none is generated. The model is still returned rather than ignored: this is
        // the sharpest form of an interceptor that does not run — an interceptor implementing only
        // IInterceptor applied to a service whose members are all async never runs at all — and
        // returning Ignore here is what kept DM0015 from ever seeing it. The generator drops it after
        // reporting.
        return new InterceptorModel(
            serviceSymbol.GetTypeDefinition(),
            ToTypeDefinition(implementationSymbol),
            interceptors,
            members,
            declarations,
            order,
            TypeParameters: TypeParameterNames(implementationSymbol));
    }

    private static InterceptorModel Refuse(string? reason) =>
        reason == null
            ? InterceptorModel.Ignore
            : InterceptorModel.Refused(reason);

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
    /// Whether any interceptor can be placed around any member.
    /// </summary>
    /// <remarks>
    /// An interface is intercepted as a whole, and one mixing synchronous, asynchronous and stream
    /// members is ordinary rather than a mistake: an interceptor implements the interfaces it can
    /// serve and has nothing to say about the rest, so those members are forwarded untouched. Only
    /// when none of them matches at all is there no wrapper worth generating.
    /// </remarks>
    private static bool AnyMemberIsIntercepted(
        IReadOnlyList<InterceptorTypeModel> interceptors, IReadOnlyList<InterceptedMemberModel> members) {

        foreach (var member in members) {
            foreach (var interceptor in interceptors) {
                if (interceptor.CanServe(member.Kind)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ReadAttribute(
        AttributeSyntax attribute,
        SyntaxTransformContext context,
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
        TypeOfExpressionSyntax typeOf, SyntaxTransformContext context, CancellationToken cancellationToken) =>
        context.SemanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type as INamedTypeSymbol;

    /// <summary>
    /// The interface to wrap. Interception works through an interface: a call the implementation
    /// makes to itself never passes through the wrapper, and a class with no interface has nothing
    /// to wrap.
    /// </summary>
    private static INamedTypeSymbol? ResolveServiceInterface(
        INamedTypeSymbol implementation, INamedTypeSymbol? explicitService, out string? unsupported) {

        unsupported = null;

        if (explicitService != null) {
            foreach (var candidate in implementation.AllInterfaces) {
                if (SymbolEqualityComparer.Default.Equals(candidate, explicitService)) {
                    return candidate;
                }
            }

            unsupported = $"'{implementation.Name}' does not implement '{explicitService.Name}'";
            return null;
        }

        var interfaces = DeclaredInterfaces(implementation);

        if (interfaces.Length == 0) {
            unsupported = $"'{implementation.Name}' implements no interface, so there is nothing to intercept";
            return null;
        }

        if (interfaces.Length > 1) {
            unsupported =
                $"'{implementation.Name}' implements more than one interface; set Service to choose which to intercept";
            return null;
        }

        return interfaces[0];
    }

    /// <summary>
    /// The interfaces the service is registered as: the ones the type declares, or failing that the
    /// ones it inherits from the nearest base that declares any.
    /// </summary>
    /// <remarks>
    /// This matches how a service registration picks its interface. Looking only at the type's own
    /// interfaces disagreed with it, and disagreed on exactly the shape that closes a generic
    /// service — <c>class StringRepo : Repo&lt;string&gt;</c> reaches its interface only through the
    /// base, and registered as <c>IRepo&lt;string&gt;</c> while interception said it implemented
    /// none.
    ///
    /// Not <c>AllInterfaces</c>, which flattens what the interfaces themselves extend and would
    /// report a plain <c>IDerived</c> as ambiguous with the <c>IBase</c> behind it.
    /// </remarks>
    private static ImmutableArray<INamedTypeSymbol> DeclaredInterfaces(INamedTypeSymbol implementation) {
        for (var type = implementation; type != null; type = type.BaseType) {
            if (type.Interfaces.Length > 0) {
                return type.Interfaces;
            }
        }

        return ImmutableArray<INamedTypeSymbol>.Empty;
    }

    /// <summary>
    /// The first type parameter carrying a constraint, or null when none does.
    /// </summary>
    /// <remarks>
    /// Every kind counts, <c>class</c> and <c>new()</c> included: the wrapper closes the
    /// implementation over its own parameters, and any constraint the implementation declares has to
    /// hold there too.
    /// </remarks>
    private static string? ConstrainedTypeParameter(INamedTypeSymbol symbol) {
        foreach (var parameter in symbol.TypeParameters) {
            if (parameter.HasReferenceTypeConstraint ||
                parameter.HasValueTypeConstraint ||
                parameter.HasNotNullConstraint ||
                parameter.HasUnmanagedTypeConstraint ||
                parameter.HasConstructorConstraint ||
                parameter.ConstraintTypes.Length > 0) {

                return parameter.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// The implementation's type parameter names, which the wrapper repeats verbatim so that its own
    /// parameters line up with the ones the service and the implementation are closed over.
    /// </summary>
    private static IReadOnlyList<string> TypeParameterNames(INamedTypeSymbol symbol) {
        if (symbol.TypeParameters.Length == 0) {
            return Array.Empty<string>();
        }

        var names = new string[symbol.TypeParameters.Length];

        for (var i = 0; i < names.Length; i++) {
            names[i] = symbol.TypeParameters[i].Name;
        }

        return names;
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

    private static List<AttributeSyntax> FindAttributes(TypeDeclarationSyntax typeDeclarationSyntax) {
        var attributes = new List<AttributeSyntax>();

        foreach (var attributeList in typeDeclarationSyntax.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var name = attribute.Name.ToString();

                if (name is "Intercept" or "InterceptAttribute") {
                    attributes.Add(attribute);
                }
            }
        }

        return attributes;
    }
}

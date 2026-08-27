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

        var attributes = FindAttributes(typeDeclarationSyntax, context.SemanticModel, cancellationToken);

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
        // Constraints come along with the parameters. The wrapper is declared over the same ones and
        // repeats their constraints, without which it could not reference what it wraps.

        var interceptorSymbols = new List<INamedTypeSymbol>();
        var order = 0;
        INamedTypeSymbol? explicitService = null;
        INamedTypeSymbol? realm = null;

        // Every [Intercept], not the first. The attribute is AllowMultiple, so stacking them is a
        // supported way to write what one attribute can also express as a params list — and reading
        // only the first dropped every interceptor after it, with nothing to say so.
        foreach (var attribute in attributes) {
            ReadAttribute(
                attribute, context, cancellationToken, interceptorSymbols, ref order, ref explicitService, ref realm);
        }

        if (interceptorSymbols.Count == 0) {
            return InterceptorModel.Ignore;
        }

        var serviceSymbol = ResolveServiceInterface(implementationSymbol, explicitService, out var unsupported);

        if (serviceSymbol == null) {
            return Refuse(unsupported, context);
        }

        if (!InterceptedMemberReader.Read(serviceSymbol, out var members, out var declarations, out unsupported)) {
            return Refuse(unsupported, context);
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
            TypeParameters: TypeParameterModels(implementationSymbol),
            Realm: (realm?.GetTypeDefinition() ??
                    RegistrationRealm(typeDeclarationSyntax, context, cancellationToken)),
            Location: LocationModel.From(context.Node));
    }

    /// <summary>
    /// The realm this class's own registration names, for an interception that names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An interception is about one implementation - that is what per-implementation interception
    /// means - so it belongs wherever that implementation's registration belongs. Without this,
    /// a realm-scoped service with an unrealmed [Intercept] put the registration in the named
    /// module and the applicator in every module except it: dead in every container that could
    /// exist, and reported by nothing, because each half was individually following the rule.
    /// </para>
    /// <para>
    /// Only consulted when [Intercept] names no realm of its own. An explicit Realm is the
    /// developer's answer and stays the answer.
    /// </para>
    /// <para>
    /// Read from the declaration rather than from the service model, because the two are built by
    /// different providers and this one has only the syntax it was handed. That also bounds what
    /// this can see: a class registered by a convention carries no service attribute, so its realm
    /// is decided at match time and is not visible from here.
    /// </para>
    /// </remarks>
    private static ITypeDefinition? RegistrationRealm(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SyntaxTransformContext context,
        CancellationToken cancellationToken) {

        foreach (var attributeList in typeDeclarationSyntax.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                if (!IsServiceAttribute(attribute, context, cancellationToken)) {
                    continue;
                }

                foreach (var argument in attribute.ArgumentList?.Arguments ??
                                         default(SeparatedSyntaxList<AttributeArgumentSyntax>)) {
                    if (argument.NameEquals?.Name.ToString() == "Realm" &&
                        argument.Expression is TypeOfExpressionSyntax realmTypeOf) {
                        return ResolveType(realmTypeOf, context, cancellationToken)?.GetTypeDefinition();
                    }
                }
            }
        }

        return null;
    }

    private static bool IsServiceAttribute(
        AttributeSyntax attribute, SyntaxTransformContext context, CancellationToken cancellationToken) {

        foreach (var serviceAttribute in ServiceAttributeTypes) {
            if (AttributeTypeMatcher.Matches(
                    context.SemanticModel, attribute, serviceAttribute, cancellationToken)) {
                return true;
            }
        }

        return false;
    }

    private static readonly ITypeDefinition[] ServiceAttributeTypes = {
        KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
        KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute,
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute,
        KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute
    };

    private static InterceptorModel Refuse(string? reason, SyntaxTransformContext context) =>
        reason == null
            ? InterceptorModel.Ignore
            : InterceptorModel.Refused(reason, LocationModel.From(context.Node));

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
        ref INamedTypeSymbol? explicitService,
        ref INamedTypeSymbol? realm) {

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
                case "Realm":
                    if (argument.Expression is TypeOfExpressionSyntax realmTypeOf) {
                        realm = ResolveType(realmTypeOf, context, cancellationToken);
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
    /// The implementation's type parameters and their constraints, which the wrapper repeats so its
    /// own parameters line up with the ones the service and the implementation are closed over.
    /// </summary>
    private static IReadOnlyList<TypeParameterModel> TypeParameterModels(INamedTypeSymbol symbol) {
        if (symbol.TypeParameters.Length == 0) {
            return Array.Empty<TypeParameterModel>();
        }

        var models = new TypeParameterModel[symbol.TypeParameters.Length];

        for (var i = 0; i < models.Length; i++) {
            models[i] = TypeParameterReader.Read(symbol.TypeParameters[i]);
        }

        return models;
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

    /// <summary>
    /// The <c>[Intercept]</c> usages on a declaration, resolved rather than string-matched.
    /// </summary>
    /// <remarks>
    /// This used to compare <c>attribute.Name.ToString()</c> against "Intercept" and
    /// "InterceptAttribute", so a qualified name, a <c>global::</c> prefix or a using alias found
    /// nothing — no wrapper, no diagnostic, and a cross-cutting concern that stopped running on a
    /// green build. It is the same mistake the service attributes carried until 1.1.0, and the same
    /// fix: ask the semantic model what the usage binds to.
    ///
    /// The declaration is reached through <c>ForAttributeWithMetadataName</c>, which resolves the
    /// attribute properly, so only this second pass ever lost it — the node was always found.
    /// </remarks>
    private static List<AttributeSyntax> FindAttributes(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {

        var attributes = new List<AttributeSyntax>();

        foreach (var attributeList in typeDeclarationSyntax.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                if (AttributeTypeMatcher.Matches(
                        semanticModel,
                        attribute,
                        KnownTypes.DependencyModules.Attributes.InterceptAttribute,
                        cancellationToken)) {
                    attributes.Add(attribute);
                }
            }
        }

        return attributes;
    }
}

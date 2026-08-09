using CSharpAuthor;
using DependencyModules.Conventions.Models;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.Conventions.Utilities;

/// <summary>
/// Turns a class or record declaration into something a convention can be matched against.
/// </summary>
/// <remarks>
/// <para>
/// The interfaces are resolved here and rendered to <see cref="ITypeDefinition"/>, because
/// assignability is a semantic question that no amount of syntax can answer — <c>: IFoo</c> names
/// any <c>IFoo</c> in scope until it is bound. What is deliberately <i>not</i> done is materialising
/// the candidate's whole transitive interface closure: only the interfaces written on the
/// declaration, plus what those interfaces themselves extend, go into the model. That keeps the
/// cached model proportional to what was written rather than to the depth of the type hierarchy,
/// which matters because these models are value-compared on every keystroke.
/// </para>
/// <para>
/// Interfaces reaching the type only through a base class are kept in a separate list, so the
/// convention decides whether they count rather than the type graph deciding for it.
/// </para>
/// </remarks>
public static class ConventionCandidateUtility {

    private static readonly string[] ServiceAttributeNames = {
        "SingletonService", "SingletonServiceAttribute",
        "ScopedService", "ScopedServiceAttribute",
        "TransientService", "TransientServiceAttribute",
        "CrossWireService", "CrossWireServiceAttribute",
    };

    /// <summary>
    /// The provider predicate. Syntax only — it runs on a great many nodes.
    /// </summary>
    /// <remarks>
    /// Rejecting a declaration with no base list is free and removes most of the population: a type
    /// implementing nothing can never be assignable to a service type. Abstract and static types are
    /// dropped silently rather than reported, because an abstract base implementing the convention's
    /// interface is the normal shape rather than a mistake. A type carrying an explicit service
    /// attribute is never a candidate; the attribute always wins.
    /// </remarks>
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (node is not ClassDeclarationSyntax and not RecordDeclarationSyntax) {
            return false;
        }

        var typeDeclaration = (TypeDeclarationSyntax)node;

        if (typeDeclaration.BaseList == null || typeDeclaration.BaseList.Types.Count == 0) {
            return false;
        }

        foreach (var modifier in typeDeclaration.Modifiers) {
            if (modifier.IsKind(SyntaxKind.StaticKeyword) ||
                modifier.IsKind(SyntaxKind.AbstractKeyword) ||
                modifier.IsKind(SyntaxKind.PrivateKeyword) ||
                modifier.IsKind(SyntaxKind.ProtectedKeyword)) {
                return false;
            }
        }

        return !HasServiceAttribute(typeDeclaration);
    }

    /// <summary>
    /// Checks only the declaration's own attribute lists.
    /// </summary>
    /// <remarks>
    /// Not <c>DescendantNodes</c>, which would also find attributes on members — a class holding a
    /// static factory method marked <c>[SingletonService]</c> would otherwise disqualify itself as a
    /// candidate for reasons that have nothing to do with the class.
    /// </remarks>
    private static bool HasServiceAttribute(TypeDeclarationSyntax typeDeclaration) {
        foreach (var attributeList in typeDeclaration.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var name = attribute.Name.ToString();
                var simpleName = name.Substring(name.LastIndexOf('.') + 1);

                if (Array.IndexOf(ServiceAttributeNames, simpleName) >= 0) {
                    return true;
                }
            }
        }

        return false;
    }

    public static ConventionCandidateModel GetCandidateModel(
        SyntaxTransformContext context, CancellationToken cancellationToken) {

        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclaration) {
            return ConventionCandidateModel.Ignore;
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol) {
            return ConventionCandidateModel.Ignore;
        }

        var declared = new List<ImplementedInterfaceModel>();
        var viaBaseClass = new List<ImplementedInterfaceModel>();

        CollectInterfaces(symbol, typeDeclaration, context, declared, viaBaseClass, cancellationToken);

        if (declared.Count == 0 && viaBaseClass.Count == 0) {
            return ConventionCandidateModel.Ignore;
        }

        return new ConventionCandidateModel(
            ImplementationTypeOf(symbol),
            declared,
            viaBaseClass,
            ServiceModelUtility.GetConstructorInfo(context, typeDeclaration, cancellationToken),
            HasAccessibleConstructor(symbol),
            LocationModel.From(typeDeclaration));
    }

    /// <summary>
    /// Splits the interfaces by how the type reaches them.
    /// </summary>
    private static void CollectInterfaces(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax typeDeclaration,
        SyntaxTransformContext context,
        List<ImplementedInterfaceModel> declared,
        List<ImplementedInterfaceModel> viaBaseClass,
        CancellationToken cancellationToken) {

        // Deduped on the type definition rather than on the arity key, which is deliberately equal
        // for every closing of one generic — IHandler<A,B> and IHandler<C,D> are distinct services.
        var seen = new HashSet<ITypeDefinition>();

        foreach (var baseTypeSyntax in typeDeclaration.BaseList!.Types) {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.SemanticModel.GetSymbolInfo(baseTypeSyntax.Type).Symbol
                is not INamedTypeSymbol baseSymbol) {
                continue;
            }

            if (baseSymbol.TypeKind == TypeKind.Interface) {
                // Written on the declaration, plus everything that interface extends. An interface
                // declaring that it extends another is a deliberate statement of substitutability,
                // so a convention naming the base interface matches this type by declaration.
                Add(declared, seen, symbol, baseSymbol, null);

                foreach (var inherited in baseSymbol.AllInterfaces) {
                    Add(declared, seen, symbol, inherited, baseSymbol.Name);
                }
            }
            else if (baseSymbol.TypeKind == TypeKind.Class) {
                foreach (var inherited in baseSymbol.AllInterfaces) {
                    Add(viaBaseClass, seen, symbol, inherited, baseSymbol.Name);
                }
            }
        }
    }

    private static void Add(
        List<ImplementedInterfaceModel> target,
        HashSet<ITypeDefinition> seen,
        INamedTypeSymbol implementation,
        INamedTypeSymbol interfaceSymbol,
        string? viaTypeName) {

        var interfaceType = RegistrationFormOf(interfaceSymbol, implementation);

        if (interfaceType == null) {
            return;
        }

        // Deduped across both lists: an interface reached by declaration is a declared match even if
        // a base class also brings it, and listing it twice would register the same service twice.
        if (!seen.Add(interfaceType)) {
            return;
        }

        target.Add(new ImplementedInterfaceModel(
            interfaceType, ConventionTypeKey.For(interfaceType), viaTypeName));
    }

    /// <summary>
    /// The form the interface is registered in: the closed construction the type implements, or the
    /// open definition when the implementation is generic and passes its own parameters straight
    /// through.
    /// </summary>
    /// <remarks>
    /// Returns null for anything in between. <c>class Handler&lt;T&gt; : IHandler&lt;Order, T&gt;</c>
    /// is neither closed nor openly registerable — the container has no partially-open registration —
    /// so the pair is dropped rather than emitted as something that would throw when the provider is
    /// built.
    /// </remarks>
    private static ITypeDefinition? RegistrationFormOf(
        INamedTypeSymbol interfaceSymbol, INamedTypeSymbol implementation) {

        var definition = interfaceSymbol.GetTypeDefinition();

        if (!ContainsTypeParameter(interfaceSymbol)) {
            return definition;
        }

        if (!implementation.IsGenericType) {
            return null;
        }

        // Every argument is one of the implementation's own parameters, used once, in order.
        var arguments = interfaceSymbol.TypeArguments;

        if (arguments.Length != implementation.TypeParameters.Length) {
            return null;
        }

        for (var i = 0; i < arguments.Length; i++) {
            if (!SymbolEqualityComparer.Default.Equals(arguments[i], implementation.TypeParameters[i])) {
                return null;
            }
        }

        return OpenFormOf(definition);
    }

    private static bool ContainsTypeParameter(INamedTypeSymbol symbol) {
        foreach (var argument in symbol.TypeArguments) {
            if (argument is ITypeParameterSymbol) {
                return true;
            }

            if (argument is INamedTypeSymbol nested && ContainsTypeParameter(nested)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites the type arguments to nothing, so the definition renders as <c>IRepo&lt;&gt;</c>.
    /// This is the shape the existing attribute path already registers open generics in.
    /// </summary>
    private static ITypeDefinition OpenFormOf(ITypeDefinition definition) =>
        new GenericTypeDefinition(
            definition.TypeDefinitionEnum,
            definition.Namespace,
            definition.Name,
            definition.TypeArguments.Select(_ => TypeDefinition.Get("", "")).ToArray());

    private static ITypeDefinition ImplementationTypeOf(INamedTypeSymbol symbol) {
        var definition = symbol.GetTypeDefinition();

        return symbol.IsGenericType ? OpenFormOf(definition) : definition;
    }

    /// <summary>
    /// Whether the container could construct this type at all.
    /// </summary>
    /// <remarks>
    /// A concrete class whose constructors are all private is the case DM0006 exists for. It is
    /// surprising in a way an abstract base is not, which is why abstract types are dropped by the
    /// predicate and this is reported.
    /// </remarks>
    private static bool HasAccessibleConstructor(INamedTypeSymbol symbol) {
        if (symbol.InstanceConstructors.Length == 0) {
            return true;
        }

        foreach (var constructor in symbol.InstanceConstructors) {
            if (constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
                or Accessibility.ProtectedOrInternal) {
                return true;
            }
        }

        return false;
    }
}

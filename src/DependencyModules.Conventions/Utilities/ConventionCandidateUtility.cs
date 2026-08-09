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
    /// <para>
    /// Abstract and static types are dropped silently rather than reported, because an abstract base
    /// implementing the convention's interface is the normal shape rather than a mistake. A type
    /// carrying an explicit service attribute is never a candidate; the attribute always wins.
    /// </para>
    /// <para>
    /// A declaration with no base list <b>is</b> a candidate. It used to be rejected here, which was
    /// free and removed most of the population — but it also meant a concrete class implementing no
    /// interface could not be registered by convention at all, and that is the whole point of
    /// <c>RegisterAll().InNamespaceOf&lt;T&gt;().AsSelf()</c>. The predicate cannot know whether any
    /// convention in the compilation selects by filter rather than by assignability, because
    /// providers cannot see each other, so the cost is paid whenever the convention package is
    /// referenced. What keeps it small is that the transform does almost nothing for these: with no
    /// base list there is no interface walk.
    /// </para>
    /// </remarks>
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (node is not ClassDeclarationSyntax and not RecordDeclarationSyntax) {
            return false;
        }

        var typeDeclaration = (TypeDeclarationSyntax)node;

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

        // No base list means no assignability to resolve, and nothing else this model needs is a
        // semantic question. Binding a symbol for every such type — which is most types in most
        // projects — was the largest remaining cost of admitting them: measured on 2,000 classes,
        // the second run after an edit took 40 ms with the symbol and 12 ms without.
        if (typeDeclaration.BaseList is not { Types.Count: > 0 }) {
            return new ConventionCandidateModel(
                typeDeclaration.GetTypeDefinition(),
                Array.Empty<ImplementedInterfaceModel>(),
                Array.Empty<ImplementedInterfaceModel>(),
                ServiceModelUtility.GetConstructorInfo(context, typeDeclaration, cancellationToken),
                DeclaresAccessibleConstructor(typeDeclaration),
                LocationModel.From(typeDeclaration),
                EnvironmentConditionUtility.GetConditions(context, typeDeclaration, cancellationToken));
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol) {
            return ConventionCandidateModel.Ignore;
        }

        var declared = new List<ImplementedInterfaceModel>();
        var viaBaseClass = new List<ImplementedInterfaceModel>();

        CollectInterfaces(symbol, typeDeclaration, context, declared, viaBaseClass, cancellationToken);

        return new ConventionCandidateModel(
            ImplementationTypeOf(symbol),
            declared,
            viaBaseClass,
            ServiceModelUtility.GetConstructorInfo(context, typeDeclaration, cancellationToken),
            HasAccessibleConstructor(symbol),
            LocationModel.From(typeDeclaration),
            EnvironmentConditionUtility.GetConditions(context, typeDeclaration, cancellationToken));
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
    /// <summary>
    /// The syntactic counterpart of <see cref="HasAccessibleConstructor"/>, for the path that has no
    /// symbol.
    /// </summary>
    /// <remarks>
    /// A type declaring no constructor has the implicit public parameterless one; a primary
    /// constructor is public; otherwise any constructor not marked private or protected will do.
    /// </remarks>
    private static bool DeclaresAccessibleConstructor(TypeDeclarationSyntax typeDeclaration) {
        if (typeDeclaration.ParameterList is { Parameters.Count: >= 0 }) {
            return true;
        }

        var declaredAny = false;

        foreach (var constructor in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>()) {
            if (constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) {
                continue;
            }

            declaredAny = true;

            var hidden = constructor.Modifiers.Any(
                m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));

            if (!hidden) {
                return true;
            }
        }

        return !declaredAny;
    }

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

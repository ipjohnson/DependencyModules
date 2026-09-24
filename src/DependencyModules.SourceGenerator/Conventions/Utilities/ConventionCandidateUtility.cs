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
public static class ConventionCandidateUtility
{
    /// <summary>
    /// Attributes that take a type out of convention matching.
    /// </summary>
    /// <remarks>
    /// The service attributes, because an explicit registration always wins — and
    /// <c>[Decorator]</c>, because a decorator is not a service. A decorator implements the
    /// interface it decorates, so a convention scanning that interface matched the decorator too and
    /// registered it as an ordinary implementation. For a generic decorator that is worse than a
    /// stray registration: it closes nothing, so it registered as the <i>open</i> generic, and
    /// decoration then refused the whole thing because an open generic registration cannot be
    /// decorated. One open generic decorator over convention-registered handlers — the ordinary
    /// MediatR and FluentValidation shape — failed at the composition root because of it.
    /// </remarks>
    private static readonly string[] ServiceAttributeNames =
    {
        "SingletonService",
        "SingletonServiceAttribute",
        "ScopedService",
        "ScopedServiceAttribute",
        "TransientService",
        "TransientServiceAttribute",
        "CrossWireService",
        "CrossWireServiceAttribute",
        "Decorator",
        "DecoratorAttribute",
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
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (node is not ClassDeclarationSyntax and not RecordDeclarationSyntax)
        {
            return false;
        }

        var typeDeclaration = (TypeDeclarationSyntax)node;

        // Only what the written modifiers settle. The transform reads the symbol of a nested type,
        // because a nested type with no access modifier is private, and a protected internal one
        // can be used by generated code.
        foreach (var modifier in typeDeclaration.Modifiers)
        {
            if (
                modifier.IsKind(SyntaxKind.StaticKeyword)
                || modifier.IsKind(SyntaxKind.AbstractKeyword)
                || modifier.IsKind(SyntaxKind.PrivateKeyword)
                || modifier.IsKind(SyntaxKind.FileKeyword)
            )
            {
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
    private static bool HasServiceAttribute(TypeDeclarationSyntax typeDeclaration)
    {
        foreach (var attributeList in typeDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                var simpleName = name.Substring(name.LastIndexOf('.') + 1);

                if (Array.IndexOf(ServiceAttributeNames, simpleName) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static ConventionCandidateModel GetCandidateModel(
        SyntaxTransformContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclaration)
        {
            return ConventionCandidateModel.Ignore;
        }

        // No base list means no assignability to resolve, and nothing else this model needs is a
        // semantic question. Binding a symbol for every such type — which is most types in most
        // projects — was the largest remaining cost of admitting them: measured on 2,000 classes,
        // the second run after an edit took 40 ms with the symbol and 12 ms without.
        if (typeDeclaration.BaseList is not { Types.Count: > 0 })
        {
            // Except for a nested or partial type. Its access, its containing type, or a constructor
            // on another declaration is not in this syntax. Both are rare.
            INamedTypeSymbol? bound = null;

            if (
                typeDeclaration.Parent is TypeDeclarationSyntax
                || typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))
            )
            {
                bound = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);

                if (bound == null || !context.GeneratedCodeCanUse(bound))
                {
                    return ConventionCandidateModel.Ignore;
                }
            }

            return new ConventionCandidateModel(
                bound != null ? ImplementationTypeOf(bound) : typeDeclaration.GetTypeDefinition(),
                Array.Empty<ImplementedInterfaceModel>(),
                Array.Empty<ImplementedInterfaceModel>(),
                ServiceModelUtility.GetConstructorInfo(context, typeDeclaration, cancellationToken),
                bound != null
                    ? ConstructorAccessOf(bound)
                    : DeclaredConstructorAccess(typeDeclaration),
                LocationModel.From(typeDeclaration),
                EnvironmentConditionUtility.GetConditions(
                    context,
                    typeDeclaration,
                    cancellationToken
                ),
                CollectAttributeKeys(context, typeDeclaration, cancellationToken)
            );
        }

        if (
            context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol
            || !context.GeneratedCodeCanUse(symbol)
        )
        {
            return ConventionCandidateModel.Ignore;
        }

        var declared = new List<ImplementedInterfaceModel>();
        var viaBaseClass = new List<ImplementedInterfaceModel>();

        CollectInterfaces(
            symbol,
            typeDeclaration,
            context,
            declared,
            viaBaseClass,
            cancellationToken
        );

        return new ConventionCandidateModel(
            ImplementationTypeOf(symbol),
            declared,
            viaBaseClass,
            ServiceModelUtility.GetConstructorInfo(context, typeDeclaration, cancellationToken),
            ConstructorAccessOf(symbol),
            LocationModel.From(typeDeclaration),
            EnvironmentConditionUtility.GetConditions(context, typeDeclaration, cancellationToken),
            CollectAttributeKeys(context, typeDeclaration, cancellationToken)
        );
    }

    /// <summary>
    /// The attribute types a declaration carries, resolved.
    /// </summary>
    /// <remarks>
    /// Resolved rather than taken as written, because matching attributes on their written name is
    /// how a namespace-qualified usage came to be silently ignored once already in this generator.
    /// Costs nothing for a type with no attributes, which is most of them, so it stays off the price
    /// of admitting every class as a candidate.
    /// </remarks>
    private static IReadOnlyList<string>? CollectAttributeKeys(
        SyntaxTransformContext context,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken cancellationToken
    )
    {
        List<string>? keys = null;

        foreach (var attributeList in typeDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (
                    ModelExtensions.GetTypeInfo(context.SemanticModel, attribute).Type
                    is not { } type
                )
                {
                    continue;
                }

                keys ??= new List<string>();
                keys.Add(ConventionTypeKey.For(type.GetTypeDefinition()));
            }
        }

        return keys;
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
        CancellationToken cancellationToken
    )
    {
        // Deduped on the type definition rather than on the arity key, which is deliberately equal
        // for every closing of one generic — IHandler<A,B> and IHandler<C,D> are distinct services.
        var seen = new HashSet<ITypeDefinition>();

        foreach (var baseTypeSyntax in typeDeclaration.BaseList!.Types)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (
                context.SemanticModel.GetSymbolInfo(baseTypeSyntax.Type).Symbol
                is not INamedTypeSymbol baseSymbol
            )
            {
                continue;
            }

            if (baseSymbol.TypeKind == TypeKind.Interface)
            {
                // Written on the declaration, plus everything that interface extends. An interface
                // declaring that it extends another is a deliberate statement of substitutability,
                // so a convention naming the base interface matches this type by declaration.
                Add(declared, seen, symbol, baseSymbol, null);

                foreach (var inherited in baseSymbol.AllInterfaces)
                {
                    Add(declared, seen, symbol, inherited, baseSymbol.Name);
                }
            }
            else if (baseSymbol.TypeKind == TypeKind.Class)
            {
                foreach (var inherited in baseSymbol.AllInterfaces)
                {
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
        string? viaTypeName
    )
    {
        var interfaceType = RegistrationFormOf(interfaceSymbol, implementation);

        if (interfaceType == null)
        {
            return;
        }

        // Deduped across both lists: an interface reached by declaration is a declared match even if
        // a base class also brings it, and listing it twice would register the same service twice.
        if (!seen.Add(interfaceType))
        {
            return;
        }

        target.Add(
            new ImplementedInterfaceModel(
                interfaceType,
                ConventionTypeKey.For(interfaceType),
                viaTypeName
            )
        );
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
        INamedTypeSymbol interfaceSymbol,
        INamedTypeSymbol implementation
    )
    {
        var definition = interfaceSymbol.GetTypeDefinition();

        if (!ContainsTypeParameter(interfaceSymbol))
        {
            return definition;
        }

        if (!implementation.IsGenericType)
        {
            return null;
        }

        // Every argument is one of the implementation's own parameters, used once, in order.
        var arguments = interfaceSymbol.TypeArguments;

        if (arguments.Length != implementation.TypeParameters.Length)
        {
            return null;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    arguments[i],
                    implementation.TypeParameters[i]
                )
            )
            {
                return null;
            }
        }

        return OpenFormOf(definition);
    }

    private static bool ContainsTypeParameter(INamedTypeSymbol symbol)
    {
        foreach (var argument in symbol.TypeArguments)
        {
            if (argument is ITypeParameterSymbol)
            {
                return true;
            }

            if (argument is INamedTypeSymbol nested && ContainsTypeParameter(nested))
            {
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
            definition.TypeArguments.Select(_ => TypeDefinition.Get("", "")).ToArray()
        );

    private static ITypeDefinition ImplementationTypeOf(INamedTypeSymbol symbol)
    {
        var definition = symbol.GetTypeDefinition();

        return symbol.IsGenericType ? OpenFormOf(definition) : definition;
    }

    /// <summary>
    /// The syntactic counterpart of <see cref="ConstructorAccessOf"/>, for the path that has no
    /// symbol.
    /// </summary>
    /// <remarks>
    /// A type that declares no constructor has the implicit public one, and a primary constructor is
    /// public. A declared constructor with no access modifier is private.
    /// </remarks>
    private static ConstructorAccess DeclaredConstructorAccess(
        TypeDeclarationSyntax typeDeclaration
    )
    {
        if (typeDeclaration.ParameterList != null)
        {
            return ConstructorAccess.Public;
        }

        var declaredAny = false;
        var widest = ConstructorAccess.None;

        foreach (var constructor in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            if (constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            {
                continue;
            }

            declaredAny = true;

            if (constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            {
                return ConstructorAccess.Public;
            }

            if (constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword)))
            {
                widest = ConstructorAccess.Assembly;
            }
        }

        return declaredAny ? widest : ConstructorAccess.Public;
    }

    /// <summary>
    /// The widest access among the constructors of the type.
    /// </summary>
    /// <remarks>
    /// A concrete class that nothing outside it can construct is the case DM0006 exists for. It is
    /// surprising in a way an abstract base is not, which is why abstract types are dropped by the
    /// predicate and this is reported.
    /// </remarks>
    private static ConstructorAccess ConstructorAccessOf(INamedTypeSymbol symbol)
    {
        var widest = ConstructorAccess.None;

        foreach (var constructor in symbol.InstanceConstructors)
        {
            switch (constructor.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return ConstructorAccess.Public;

                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    widest = ConstructorAccess.Assembly;
                    break;
            }
        }

        return widest;
    }
}

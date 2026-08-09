using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class ServiceModelUtility {
    /// <summary>
    /// Interfaces that describe a capability rather than a role, keyed by namespace and name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passed over when choosing a service type nobody named. Writing <c>: IDisposable</c> says the
    /// class cleans up after itself; it does not say <c>IDisposable</c> is what callers ask for.
    /// Without this, <c>class OrderedPool : IDisposable, IPool</c> registers as <c>IDisposable</c>,
    /// because the first interface in the declaration wins.
    /// </para>
    /// <para>
    /// A list rather than the namespace rule <c>AsSelfWithInterfaces</c> uses, because the two are
    /// not the same problem. That expansion is additive — excluding too much costs a bonus
    /// registration. This is an exclusive choice, so excluding too much means the interface the
    /// developer wanted is not registered at all. <c>System</c> holds plenty of interfaces that are
    /// genuinely service roles: <c>IEqualityComparer&lt;T&gt;</c>, <c>IJsonTypeInfoResolver</c>,
    /// <c>IHttpClientFactory</c>. Precision matters more here than a rule stated in one sentence.
    /// </para>
    /// <para>
    /// The list is short and stays short. These are the BCL's language and framework integration
    /// points, and the set has barely moved in twenty years — unlike the open-ended set of
    /// interfaces a type happens to reach, which is what makes the namespace rule right over there.
    /// </para>
    /// <para>
    /// <c>IEnumerable</c> earns its place twice over: registering a service as
    /// <c>IEnumerable&lt;T&gt;</c> collides with how the container represents "every registration of
    /// T".
    /// </para>
    /// <para>
    /// A service type the developer names is untouched — <c>[SingletonService(As =
    /// typeof(IDisposable))]</c> still registers <c>IDisposable</c>. This governs inference only.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> _capabilityInterfaces = new() {
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.ICloneable",
        "System.IComparable",            // covers IComparable<T>, same name
        "System.IEquatable",
        "System.IConvertible",
        "System.IFormattable",
        "System.ISpanFormattable",
        "System.IParsable",
        "System.ISpanParsable",
        "System.Collections.IEnumerable",
        "System.Collections.Generic.IEnumerable",
        "System.Runtime.Serialization.ISerializable",
        "System.ComponentModel.INotifyPropertyChanged",
        "System.ComponentModel.INotifyPropertyChanging",
        "System.Collections.Specialized.INotifyCollectionChanged"
    };

    private static readonly ITypeDefinition _crossWireService =
        KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute;

    private static readonly ITypeDefinition _serializerService =
        KnownTypes.Microsoft.TextJson.JsonSourceGenerationOptionsAttribute;

    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute, KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute, KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
    };

    public static ServiceModel? GetServiceModel(
        SyntaxTransformContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is ClassDeclarationSyntax or RecordDeclarationSyntax) {
            return GetClassDeclarationServiceModel(context, cancellationToken);
        }

        if (context.Node is MethodDeclarationSyntax methodDeclarationSyntax) {
            return MethodDeclarationServiceModel(context, methodDeclarationSyntax, cancellationToken);
        }

        return null;
    }

    private static ServiceModel? MethodDeclarationServiceModel(SyntaxTransformContext context, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken) {
        // only support public or internal factory methods
        if (methodDeclarationSyntax.Modifiers.Any(
                m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))) {
            return null;
        }

        // only support static methods
        if (!methodDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) {
            return null;
        }

        var returnType = methodDeclarationSyntax.ReturnType.GetTypeDefinition(context);
        var factoryModel = GetFactoryModel(context, methodDeclarationSyntax, cancellationToken);

        if (returnType == null || factoryModel == null) {
            return null;
        }

        var models =
            AttributeModelHelper.GetAttributeModels(context, context.Node, cancellationToken);

        return new ServiceModel(
            returnType,
            null,
            factoryModel, null,
            GetRegistrations(context, returnType, models, cancellationToken),
            RegistrationFeature.None);
    }

    private static ServiceFactoryModel? GetFactoryModel(SyntaxTransformContext context, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken) {
        var factoryClass = methodDeclarationSyntax.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (factoryClass == null) {
            return null;
        }

        var factoryType = GetTypeDeclarationDefinition(factoryClass);

        return new ServiceFactoryModel(
            factoryType,
            methodDeclarationSyntax.Identifier.ToString().Trim('"'),
            methodDeclarationSyntax.GetMethodParameters(context, cancellationToken));
    }

    /// <summary>
    /// Flags describing why the container could never construct this type, so the generator can
    /// report it instead of emitting a registration that fails when the provider is built.
    /// </summary>
    private static RegistrationFeature GetConstructionFeatures(SyntaxNode node) {
        if (node is not TypeDeclarationSyntax typeDeclarationSyntax) {
            return RegistrationFeature.None;
        }

        var features = RegistrationFeature.None;

        if (typeDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) {
            features |= RegistrationFeature.StaticImplementation;
        }
        else if (typeDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))) {
            features |= RegistrationFeature.AbstractImplementation;
        }

        return features;
    }

    private static ServiceModel? GetClassDeclarationServiceModel(SyntaxTransformContext context, CancellationToken cancellationToken) {
        var classDefinition = GetClassDefinition(context);

        if (classDefinition == null) {
            return null;
        }

        var attributes =
            AttributeModelHelper.GetAttributeModels(context, context.Node, cancellationToken);

        var registrations = GetRegistrations(context, classDefinition, attributes, cancellationToken);

        if (registrations.Count == 0) {
            return new ServiceModel(
                classDefinition,
                GetConstructorInfo(context, context.Node, cancellationToken),
                null,
                FactoryOutput,
                new[] {
                    new ServiceRegistrationModel(
                        KnownTypes.Microsoft.TextJson.IJsonTypeInfoResolver,
                        ServiceLifestyle.Transient
                    )
                },
                RegistrationFeature.AutoRegisterSourceGenerator
            );
        }

        FactoryOutputDelegate? factoryOutput = null;

        if (registrations.Any(
                r => r.ServiceType.Equals(KnownTypes.Microsoft.TextJson.IJsonTypeInfoResolver))) {
            factoryOutput = FactoryOutput;
        }

        return new ServiceModel(classDefinition,
            GetConstructorInfo(context, context.Node, cancellationToken),
            null,
            factoryOutput,
            registrations,
            GetConstructionFeatures(context.Node),
            EnvironmentConditionUtility.GetConditions(context, context.Node, cancellationToken));
    }

    /// <summary>
    /// The constructor the container should use, read from the type's own members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Direct members rather than <c>DescendantNodes</c>, which walks the entire subtree — every
    /// method body, every statement, every expression — to find nodes that can only ever be direct
    /// children. Measured on a 2,000-class compilation of ordinary classes, that walk was the
    /// dominant cost of the candidate transform: the second generator run after editing one file
    /// took 73 ms with it and 12 ms without.
    /// </para>
    /// <para>
    /// It was also wrong. <c>DescendantNodes</c> finds the constructors of <i>nested</i> types, so a
    /// class containing a nested class with a constructor could be registered against the nested
    /// type's parameters.
    /// </para>
    /// </remarks>
    public static ConstructorInfoModel? GetConstructorInfo(SyntaxTransformContext context, SyntaxNode node, CancellationToken cancellationToken) {
        var constructorList = new List<ConstructorDeclarationSyntax>();

        var members = node is TypeDeclarationSyntax declaration
            ? declaration.Members.OfType<ConstructorDeclarationSyntax>()
            : node.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

        foreach (var constructor in members) {
            if (constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) {
                continue;
            }

            if (constructor.AttributeLists.Any(attributeList =>
                    attributeList.Attributes.Any(
                        a => a.Name.ToString() == "ActivatorUtilitiesConstructorAttribute" ||
                             a.Name.ToString() == "ActivatorUtilitiesConstructor"))) {

                return new ConstructorInfoModel(constructor.GetMethodParameters(context, cancellationToken));
            }

            constructorList.Add(constructor);
        }

        if (node is TypeDeclarationSyntax { ParameterList.Parameters.Count: > 0 } typeDeclarationSyntax) {
            return new ConstructorInfoModel(
                typeDeclarationSyntax.ParameterList.GetParameters(context, cancellationToken));
        }
        
        if (constructorList.Count == 0) {
            return new ConstructorInfoModel(ImmutableArray<ParameterInfoModel>.Empty);
        }

        if (constructorList.Count == 1) {
            var constructor = constructorList[0];

            return new ConstructorInfoModel(constructor.GetMethodParameters(context, cancellationToken));
        }

        constructorList.Sort(
            (a, b) =>
                a.ParameterList.Parameters.Count.CompareTo(b.ParameterList.Parameters.Count));

        return new ConstructorInfoModel(
            constructorList.Last().GetMethodParameters(context, cancellationToken)
        );
    }

    private static IOutputComponent? FactoryOutput(ServiceModel servicemodel, ServiceRegistrationModel registrationmodel) {
        var signature = "_ => ";

        if (registrationmodel.Key != null) {
            signature = "(_,_) => ";
        }

        var component = CodeOutputComponent.Get(
            $"{signature}{servicemodel.ImplementationType.Namespace}.{servicemodel.ImplementationType.Name}.Default");

        return component;
    }

    private static ITypeDefinition? GetClassDefinition(SyntaxTransformContext context) {
        ITypeDefinition? classTypeDefinition = null;

        if (context.Node is TypeDeclarationSyntax typeDeclarationSyntax) {
            classTypeDefinition = GetTypeDeclarationDefinition(typeDeclarationSyntax);
        }

        return classTypeDefinition;
    }

    private static ITypeDefinition GetTypeDeclarationDefinition(TypeDeclarationSyntax typeDeclarationSyntax) {
        ITypeDefinition classTypeDefinition;
        var declaredName = GetDeclaredName(typeDeclarationSyntax);

        if (typeDeclarationSyntax.TypeParameterList is { Parameters.Count: > 0 }) {
            classTypeDefinition =
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    typeDeclarationSyntax.GetNamespace(),
                    declaredName,
                    typeDeclarationSyntax.TypeParameterList.Parameters.Select(_ => TypeDefinition.Get("", ""))
                        .ToArray()
                );
        }
        else {
            classTypeDefinition = TypeDefinition.Get(typeDeclarationSyntax.GetNamespace(), declaredName);
        }

        return classTypeDefinition;
    }

    /// <summary>
    /// The type's name qualified by any containing types, so a nested service is referenced as
    /// Outer.Inner rather than Inner, which would resolve against the namespace and fail to compile.
    /// </summary>
    private static string GetDeclaredName(TypeDeclarationSyntax typeDeclarationSyntax) {
        var name = typeDeclarationSyntax.Identifier.ToString();

        foreach (var containingType in typeDeclarationSyntax.Ancestors().OfType<TypeDeclarationSyntax>()) {
            name = containingType.Identifier + "." + name;
        }

        return name;
    }

    private static List<ServiceRegistrationModel> GetRegistrations(SyntaxTransformContext context, ITypeDefinition classDefinition, IReadOnlyList<AttributeModel> attributes, CancellationToken cancellationToken) {
        var list = new List<ServiceRegistrationModel>();

        foreach (var attributeSyntax in
                 context.Node.DescendantNodes().OfType<AttributeSyntax>()) {
            foreach (var typeDefinition in _attributeTypes) {
                cancellationToken.ThrowIfCancellationRequested();

                if (attributeSyntax.Name.ToString() == typeDefinition.Name ||
                    attributeSyntax.Name + "Attribute" == typeDefinition.Name) {
                    list.Add(GetServiceRegistration(context, attributeSyntax, classDefinition));
                }
            }

            if (attributeSyntax.Name.ToString() == _crossWireService.Name ||
                attributeSyntax.Name + "Attribute" == _crossWireService.Name) {
                list.AddRange(GetCrossWiredService(context, attributeSyntax, classDefinition));
            }
        }

        return list;
    }

    private static IEnumerable<ServiceRegistrationModel> GetCrossWiredService(SyntaxTransformContext context, AttributeSyntax attributeSyntax, ITypeDefinition classDefinition) {

        RegistrationType? registrationType = null;
        ITypeDefinition? realm = null;
        object? key = null;
        ServiceLifestyle lifestyle = ServiceLifestyle.Singleton;
        var namespaces = new List<string>();

        if (attributeSyntax.ArgumentList != null) {
            foreach (var argumentSyntax in attributeSyntax.ArgumentList.Arguments) {
                if (argumentSyntax.NameEquals != null) {
                    switch (argumentSyntax.NameEquals.Name.ToString()) {
                        case "Key":
                            key = argumentSyntax.Expression.ToString();
                            if (argumentSyntax.Expression is MemberAccessExpressionSyntax accessExpressionSyntax) {
                                var type = accessExpressionSyntax.GetTypeDefinition(context);
                                if (type != null) {
                                    namespaces.AddRange(type.KnownNamespaces);
                                }
                            }
                            break;

                        case "Using":
                            registrationType =
                                BaseSourceGenerator.GetRegistrationType(argumentSyntax.Expression.ToString());
                            break;

                        case "Lifetime":
                            lifestyle = GetLifestyle(argumentSyntax.Expression.ToString());
                            break;

                        case "Realm":
                            if (argumentSyntax.Expression is TypeOfExpressionSyntax realmType) {
                                realm = realmType.Type.GetTypeDefinition(context);
                            }
                            break;
                    }
                }
            }
        }

        if (context.Node is TypeDeclarationSyntax { BaseList: not null } typeDeclarationSyntax) {
            foreach (var baseTypeSyntax in typeDeclarationSyntax.BaseList.Types) {
                var type = baseTypeSyntax.Type.GetTypeDefinition(context);

                if (type?.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition) {
                    yield return new ServiceRegistrationModel(
                        type,
                        lifestyle,
                        registrationType,
                        realm,
                        key,
                        true,
                        namespaces
                    );
                }
            }
        }
    }

    private static ServiceLifestyle GetLifestyle(string toString) {
        // The value arrives as written in source, normally qualified: "ServiceLifetime.Scoped".
        // Parsing that whole string fails, and the silent fallback below then registered every
        // cross-wired service as a singleton regardless of the lifetime the developer asked for.
        var separatorIndex = toString.LastIndexOf('.');

        var value = separatorIndex >= 0
            ? toString.Substring(separatorIndex + 1).Trim()
            : toString.Trim();

        if (Enum.TryParse(value, out ServiceLifestyle lifestyle)) {
            return lifestyle;
        }

        return ServiceLifestyle.Singleton;
    }

    private static ServiceRegistrationModel GetServiceRegistration(SyntaxTransformContext context, AttributeSyntax attributeSyntax, ITypeDefinition classDefinition) {
        var lifestyle = ServiceLifestyle.Transient;

        if (attributeSyntax.Name.ToString().StartsWith("Singleton")) {
            lifestyle = ServiceLifestyle.Singleton;
        }
        else if (attributeSyntax.Name.ToString().StartsWith("Scoped")) {
            lifestyle = ServiceLifestyle.Scoped;
        }

        ITypeDefinition? registration = null;
        RegistrationType? registrationType = null;
        ITypeDefinition? realm = null;
        object? key = null;
        var namespaces = new List<string>();

        if (attributeSyntax.ArgumentList != null) {
            foreach (var argumentSyntax in attributeSyntax.ArgumentList.Arguments) {
                if (argumentSyntax.NameEquals != null) {
                    switch (argumentSyntax.NameEquals.Name.ToString()) {
                        case "Key":
                            key = argumentSyntax.Expression.ToString();

                            if (argumentSyntax.Expression is MemberAccessExpressionSyntax accessExpressionSyntax) {
                                var type = accessExpressionSyntax.GetTypeDefinition(context);
                                if (type != null) {
                                    namespaces.AddRange(type.KnownNamespaces);
                                }
                            }
                            break;
                        case "Using":
                            registrationType =
                                BaseSourceGenerator.GetRegistrationType(argumentSyntax.Expression.ToString());
                            break;

                        case "As":
                            if (argumentSyntax.Expression is TypeOfExpressionSyntax typeOfExpression) {
                                registration = typeOfExpression.Type.GetTypeDefinition(context);

                                if (registration is GenericTypeDefinition) {
                                    registration = ReplaceGenericParametersForRegistration(registration);
                                }
                            }
                            break;

                        case "Realm":
                            if (argumentSyntax.Expression is TypeOfExpressionSyntax realmType) {
                                realm = realmType.Type.GetTypeDefinition(context);
                            }
                            break;
                    }
                }
            }
        }

        return new ServiceRegistrationModel(
            registration ?? GetServiceTypeFromClass(context, classDefinition),
            lifestyle,
            registrationType,
            realm,
            key,
            false,
            namespaces
        );
    }

    private static ITypeDefinition GetServiceTypeFromClass(
        SyntaxTransformContext context, ITypeDefinition classDefinition) {
        return GetBaseTypeRegistration(context) ?? classDefinition;
    }

    /// <summary>
    /// The service type to register a class as when the developer did not name one: the first
    /// declared interface that is not a <see cref="_capabilityInterfaces">capability</see>, else the
    /// first one a base class provides.
    /// </summary>
    private static ITypeDefinition? GetBaseTypeRegistration(SyntaxTransformContext context) {
        if (context.Node is TypeDeclarationSyntax { BaseList: not null } typeDeclarationSyntax) {
            INamedTypeSymbol? baseClassSymbol = null;

            foreach (var baseTypeSyntax in typeDeclarationSyntax.BaseList.Types) {
                var symbolInfo = ModelExtensions.GetSymbolInfo(context.SemanticModel, baseTypeSyntax.Type);

                if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol) {
                    var baseTypeDefinition =
                        namedTypeSymbol.GetTypeDefinitionFromNamedSymbol();

                    // only auto register interfaces
                    if (baseTypeDefinition is { TypeDefinitionEnum: TypeDefinitionEnum.InterfaceDefinition }) {
                        // Passed over rather than remembered: a skipped interface must not become the
                        // symbol walked below, or IEnumerable<int> would hand back IEnumerable.
                        if (SkipInterface(baseTypeDefinition)) {
                            continue;
                        }

                        if (baseTypeDefinition is GenericTypeDefinition) {
                            baseTypeDefinition = ReplaceGenericParametersForRegistration(baseTypeDefinition);
                        }

                        return baseTypeDefinition;
                    }

                    baseClassSymbol = namedTypeSymbol;
                }
            }

            if (baseClassSymbol != null) {
                return GetBaseInterface(context, baseClassSymbol);
            }
        }

        return null;
    }


    private static ITypeDefinition? GetBaseInterface(SyntaxTransformContext context, INamedTypeSymbol baseTypeSymbol) {
        foreach (var interfaceSymbol in baseTypeSymbol.Interfaces) {
            var interfaceType =
                interfaceSymbol.GetTypeDefinitionFromNamedSymbol();

            // only auto register interfaces
            if (interfaceType == null ||
                SkipInterface(interfaceType)) {
                continue;
            }

            if (interfaceType is GenericTypeDefinition) {
                interfaceType = ReplaceGenericParametersForRegistration(interfaceType);
            }

            return interfaceType;
        }

        if (baseTypeSymbol.BaseType == null) {
            return null;
        }

        return GetBaseInterface(context, baseTypeSymbol.BaseType);
    }

    /// <summary>
    /// Whether an interface is passed over when choosing a service type nobody named.
    /// </summary>
    /// <remarks>
    /// Matched on namespace and name so one entry covers a generic and its closings —
    /// <c>IEquatable&lt;Money&gt;</c> and <c>IEquatable&lt;T&gt;</c> both render as
    /// <c>System.IEquatable</c>.
    /// </remarks>
    private static bool SkipInterface(ITypeDefinition interfaceType) =>
        _capabilityInterfaces.Contains($"{interfaceType.Namespace}.{interfaceType.Name}");

    private static ITypeDefinition ReplaceGenericParametersForRegistration(ITypeDefinition registration) {
        var argumentTypes =
            registration.TypeArguments.Select(
                _ => _ is TypeParameterDefinition ? TypeDefinition.Get("", "") : _).ToArray();

        registration = new GenericTypeDefinition(
            registration.TypeDefinitionEnum,
            registration.Namespace,
            registration.Name,
            argumentTypes
        );
        
        return registration;
    }
}
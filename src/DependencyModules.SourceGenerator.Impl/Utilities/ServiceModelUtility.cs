using System.Collections.Immutable;
using System.ComponentModel;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class ServiceModelUtility {
    private static ITypeDefinition[] _skipTypes = new[] {
        TypeDefinition.Get(typeof(INotifyPropertyChanged))
    };

    private static readonly ITypeDefinition _crossWireService =
        KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute;

    private static readonly ITypeDefinition _serializerService =
        KnownTypes.Microsoft.TextJson.JsonSourceGenerationOptionsAttribute;

    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute, KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute, KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
    };

    public static ServiceModel? GetServiceModel(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is ClassDeclarationSyntax) {
            return GetClassDeclarationServiceModel(context, cancellationToken);
        }

        if (context.Node is MethodDeclarationSyntax methodDeclarationSyntax) {
            return MethodDeclarationServiceModel(context, methodDeclarationSyntax, cancellationToken);
        }

        return null;
    }

    private static ServiceModel? MethodDeclarationServiceModel(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken) {
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

    private static ServiceFactoryModel? GetFactoryModel(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken) {
        var factoryClass = methodDeclarationSyntax.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (factoryClass == null) {
            return null;
        }

        var factoryType = GetClassTypeDefinition(factoryClass);

        return new ServiceFactoryModel(
            factoryType,
            methodDeclarationSyntax.Identifier.ToString().Trim('"'),
            methodDeclarationSyntax.GetMethodParameters(context, cancellationToken));
    }

    private static ServiceModel? GetClassDeclarationServiceModel(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
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
                GetConstructor(context, cancellationToken),
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
            GetConstructor(context, cancellationToken),
            null,
            factoryOutput,
            registrations,
            RegistrationFeature.None);
    }

    private static ConstructorInfoModel? GetConstructor(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var constructorList = new List<ConstructorDeclarationSyntax>();

        foreach (var constructor in context.Node.Ancestors().OfType<ConstructorDeclarationSyntax>()) {
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

        if (context.Node is ClassDeclarationSyntax { ParameterList: not null } classDeclarationSyntax) {
            return new ConstructorInfoModel(
                classDeclarationSyntax.ParameterList.GetParameters(context, cancellationToken));
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

    private static ITypeDefinition? GetClassDefinition(GeneratorSyntaxContext context) {
        ITypeDefinition? classTypeDefinition = null;

        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            classTypeDefinition = GetClassTypeDefinition(classDeclarationSyntax);
        }

        return classTypeDefinition;
    }

    private static ITypeDefinition GetClassTypeDefinition(ClassDeclarationSyntax classDeclarationSyntax) {
        ITypeDefinition classTypeDefinition;
        if (classDeclarationSyntax.TypeParameterList is { Parameters.Count: > 0 }) {
            classTypeDefinition =
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    classDeclarationSyntax.GetNamespace(),
                    classDeclarationSyntax.Identifier.ToString(),
                    classDeclarationSyntax.TypeParameterList.Parameters.Select(_ => TypeDefinition.Get("", ""))
                        .ToArray()
                );
        }
        else {
            classTypeDefinition = TypeDefinition.Get(classDeclarationSyntax.GetNamespace(),
                classDeclarationSyntax.Identifier.ToString());
        }

        return classTypeDefinition;
    }

    private static List<ServiceRegistrationModel> GetRegistrations(GeneratorSyntaxContext context, ITypeDefinition classDefinition, IReadOnlyList<AttributeModel> attributes, CancellationToken cancellationToken) {
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

    private static IEnumerable<ServiceRegistrationModel> GetCrossWiredService(GeneratorSyntaxContext context, AttributeSyntax attributeSyntax, ITypeDefinition classDefinition) {

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

                        case "With":
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

        if (context.Node is ClassDeclarationSyntax { BaseList: not null } classDeclarationSyntax) {
            foreach (var baseTypeSyntax in classDeclarationSyntax.BaseList.Types) {
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
        if (Enum.TryParse(toString, out ServiceLifestyle lifestyle)) {
            return lifestyle;
        }

        return ServiceLifestyle.Singleton;
    }

    private static ServiceRegistrationModel GetServiceRegistration(GeneratorSyntaxContext context, AttributeSyntax attributeSyntax, ITypeDefinition classDefinition) {
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
        GeneratorSyntaxContext context, ITypeDefinition classDefinition) {
        return GetBaseTypeRegistration(context) ?? classDefinition;
    }

    private static ITypeDefinition? GetBaseTypeRegistration(GeneratorSyntaxContext context) {
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            if (classDeclarationSyntax.BaseList != null) {
                INamedTypeSymbol? baseTypeSymbol = null;

                foreach (var baseTypeSyntax in classDeclarationSyntax.BaseList.Types) {
                    var symbolInfo = ModelExtensions.GetSymbolInfo(context.SemanticModel, baseTypeSyntax.Type);

                    if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol) {
                        var baseTypeDefinition =
                            namedTypeSymbol.GetTypeDefinitionFromNamedSymbol();

                        // only auto register interfaces
                        if (baseTypeDefinition is { TypeDefinitionEnum: TypeDefinitionEnum.InterfaceDefinition } &&
                            !SkipInterface(baseTypeDefinition)) {
                            if (baseTypeDefinition is GenericTypeDefinition) {
                                baseTypeDefinition = ReplaceGenericParametersForRegistration(baseTypeDefinition);
                            }

                            return baseTypeDefinition;
                        }

                        baseTypeSymbol = namedTypeSymbol;
                    }
                }

                if (baseTypeSymbol != null) {
                    return GetBaseInterface(context, baseTypeSymbol);
                }
            }
        }

        return null;
    }


    private static ITypeDefinition? GetBaseInterface(GeneratorSyntaxContext context, INamedTypeSymbol baseTypeSymbol) {
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

    private static bool SkipInterface(ITypeDefinition interfaceType) {
        return _skipTypes.Any(type => type.Equals(interfaceType));
    }

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
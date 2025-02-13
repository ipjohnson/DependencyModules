using System.ComponentModel;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class ServiceModelUtility {
    private static ITypeDefinition[] _skipTypes = new [] { TypeDefinition.Get(typeof(INotifyPropertyChanged))};
    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute, KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute, KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute
    };
    
    public static ServiceModel? GetServiceModel(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var classDefinition = GetClassDefinition(context);

        if (classDefinition == null) {
            return null;
        }
        
        var registrations = GetRegistrations(context, classDefinition, cancellationToken);

        return new ServiceModel(classDefinition, registrations);
    }
    
    private static ITypeDefinition? GetClassDefinition(GeneratorSyntaxContext context) {
        ITypeDefinition? classTypeDefinition = null;

        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
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
        }

        return classTypeDefinition;
    }
    
    private static List<ServiceRegistrationModel> GetRegistrations(GeneratorSyntaxContext context, ITypeDefinition classDefinition, CancellationToken cancellationToken) {
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
        }

        return list;
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

        if (attributeSyntax.ArgumentList != null) {
            foreach (var argumentSyntax in attributeSyntax.ArgumentList.Arguments) {
                if (argumentSyntax.NameEquals != null) {
                    switch (argumentSyntax.NameEquals.Name.ToString()) {
                        case "Key":
                            key = argumentSyntax.Expression.ToString();
                            break;
                        case "With":
                            registrationType = 
                                BaseSourceGenerator.GetRegistrationType(argumentSyntax.Expression.ToString());
                            break;

                        case "ServiceType":
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
            registration ?? GetServieTypeFromClass(context, classDefinition),
            lifestyle,
            registrationType,
            realm,
            key
        );
    }
    
    private static ITypeDefinition GetServieTypeFromClass(
        GeneratorSyntaxContext context, ITypeDefinition classDefinition) {
        return GetBaseTypeRegistration(context) ?? classDefinition;
    }


    private static ITypeDefinition? GetBaseTypeRegistration(GeneratorSyntaxContext context) {
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            if (classDeclarationSyntax.BaseList != null) {
                INamedTypeSymbol? baseTypeSymbol = null;
                
                foreach (var baseTypeSyntax in classDeclarationSyntax.BaseList.Types) {
                    var symbolInfo = context.SemanticModel.GetSymbolInfo(baseTypeSyntax.Type);

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
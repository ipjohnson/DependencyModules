using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator;

public class ServiceSourceGenerator : BaseAttributeSourceGenerator<ServiceModel> {
    private readonly IEqualityComparer<ServiceModel> _serviceEqualityComparer = new ServiceModelComparer();

    private static ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute,
        KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute,
        KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute
    };
    
    protected override IEnumerable<ITypeDefinition> AttributeTypes() => _attributeTypes;

    protected override void GenerateSourceOutput(
        SourceProductionContext arg1, 
        (ModuleEntryPointModel Left, ImmutableArray<ServiceModel> Right) arg2) {
        
        var writer = new DependencyFileWriter();
        
        var output = writer.Write(arg2.Left, arg2.Right, "Module");
        
        arg1.AddSource(arg2.Left.EntryPointType.Name + ".Dependencies.cs", output);
    }

    protected override IEqualityComparer<ServiceModel> GetComparer() {
        return _serviceEqualityComparer;
    }

    protected override ServiceModel GenerateAttributeModel(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var classDefinition = GetClassDefinition(context);

        var registrations = GetRegistrations(context, classDefinition, cancellationToken);
        
        return new ServiceModel(classDefinition, registrations);
    }

    private static List<ServiceRegistrationModel> GetRegistrations(
        GeneratorSyntaxContext context, ITypeDefinition classDefinition, CancellationToken cancellationToken) {
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
        var registrationType = ServiceLifestyle.Transient;

        if (attributeSyntax.Name.ToString().StartsWith("Singleton")) {
            registrationType = ServiceLifestyle.Singleton;
        }
        else if (attributeSyntax.Name.ToString().StartsWith("Scoped")) {
            registrationType = ServiceLifestyle.Scoped;
        }
        
        ITypeDefinition? registration = null;
        bool registerWithTry = true;
        bool routedRegistration = false;
        ITypeDefinition? realm = null;
        object? key = null;

        if (attributeSyntax.ArgumentList != null) {
            foreach (var argumentSyntax in attributeSyntax.ArgumentList.Arguments) {
                if (argumentSyntax.NameEquals != null) {
                    
                    switch (argumentSyntax.NameEquals.Name.ToString()) {
                        case "Key":
                            key = argumentSyntax.Expression.ToString();
                            break;
                        case "UseTry":
                            registerWithTry = argumentSyntax.Expression.ToString() == "true";
                            break;
                        case "ServiceType":
                            if (argumentSyntax.Expression is TypeOfExpressionSyntax typeOfExpression) {
                                registration = typeOfExpression.Type.GetTypeDefinition(context);
                            }
                            break;
                        case "Realm":
                            if (argumentSyntax.Expression is TypeOfExpressionSyntax realmType) {
                                realm = realmType.Type.GetTypeDefinition(context);
                            }
                            break;
                        case "RoutedRegistration":
                            routedRegistration = argumentSyntax.Expression.ToString() == "true";
                            break;
                    }
                }
            }
        }
        
        return new ServiceRegistrationModel(
            registration ?? classDefinition,
            registrationType,
            registerWithTry,
            routedRegistration,
            realm,
            key
        );
    }

    private ITypeDefinition GetClassDefinition(GeneratorSyntaxContext context) {
        ITypeDefinition classTypeDefinition;
        
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;
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
}
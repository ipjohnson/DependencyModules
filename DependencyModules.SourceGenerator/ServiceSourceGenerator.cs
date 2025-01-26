using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator;

public class ServiceSourceGenerator : BaseAttributeSourceGenerator<ServiceModel> {

    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute, KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute, KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute
    };

    private readonly IEqualityComparer<ServiceModel> _serviceEqualityComparer = new ServiceModelComparer();

    protected override IEnumerable<ITypeDefinition> AttributeTypes() {
        return _attributeTypes;
    }

    protected override void GenerateSourceOutput(
        SourceProductionContext context,
        ((ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right) Left, ImmutableArray<ServiceModel> Right) inputData) {

        var writer = new DependencyFileWriter();

        var output = writer.Write(inputData.Left.Left, inputData.Left.Right, inputData.Right, "Module");

        context.AddSource(inputData.Left.Left.EntryPointType.Name + ".Dependencies.g.cs", output);
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
        bool? registerWithTry = null;
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

                                if (registration is GenericTypeDefinition) {
                                    var argumentTypes =
                                        registration.TypeArguments.Select(
                                            _ => _ is TypeParameterDefinition ? 
                                                TypeDefinition.Get("", "") : _).ToArray();
                                    
                                    registration = new GenericTypeDefinition(
                                        registration.TypeDefinitionEnum,
                                        registration.Namespace,
                                        registration.Name,
                                        argumentTypes
                                    );
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
            registration ?? classDefinition,
            registrationType,
            registerWithTry,
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
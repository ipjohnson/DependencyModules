using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl;

public abstract class BaseSourceGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var incrementalValueProvider = CreateSourceValueProvider(context);
        var dependencyConfigurationProvider = CreateConfigurationValueProvider(context);

        var valuesProvider = incrementalValueProvider.Combine(dependencyConfigurationProvider);

        foreach (var attributeSourceGenerator in AttributeSourceGenerators()) {
            attributeSourceGenerator.SetupGenerator(context, valuesProvider);
        }

        SetupRootGenerator(context, valuesProvider);
    }

    protected abstract IEnumerable<ISourceGenerator> AttributeSourceGenerators();

    private IncrementalValueProvider<DependencyModuleConfigurationModel> CreateConfigurationValueProvider(IncrementalGeneratorInitializationContext context) {
        return context.AnalyzerConfigOptionsProvider.Select((options, _) => {
            RegistrationType defaultRegistrationType = RegistrationType.Add;

            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegistrationType", out var value)) {
                defaultRegistrationType = GetRegistrationType(value);
            }

            return new DependencyModuleConfigurationModel(defaultRegistrationType);
        }).WithComparer(new DependencyModuleConfigurationModelComparer());
    }

    protected virtual void SetupRootGenerator(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> valuesProvider) { }

    private IncrementalValuesProvider<ModuleEntryPointModel> CreateSourceValueProvider(IncrementalGeneratorInitializationContext context) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.DependencyModules.Attributes.DependencyModuleAttribute);

        return context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateEntryPointModel
        ).WithComparer(new ModuleEntryPointModelComparer());
    }

    protected virtual ModuleEntryPointModel GenerateEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();

        List<AttributeModel>? attributes = null;

        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            attributes = AttributeModelHelper
                .GetAttributes(context, classDeclarationSyntax.AttributeLists, cancellation)
                .ToList();
        }

        var (onlyRealm, registrationType, generateAttribute) = GetDependencyFlags(context);
        var implementsEqualsFlag = GetEqualsFlag(context);
        var parameters = GetConstructorParameters(context);
        var properties = GetProperties(context);

        return new ModuleEntryPointModel(
            ((ClassDeclarationSyntax)context.Node).GetTypeDefinition(),
            onlyRealm,
            registrationType,
            generateAttribute,
            parameters,
            implementsEqualsFlag,
            properties,
            attributes ?? new());
    }

    private List<PropertyInfoModel> GetProperties(GeneratorSyntaxContext context) {
        var propertyList = new List<PropertyInfoModel>();

        foreach (var propertyDeclarationSyntax in
                 context.Node.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList()) {
            var setter =
                propertyDeclarationSyntax.AccessorList?.Accessors.FirstOrDefault(
                    x => x.IsKind(SyntaxKind.SetAccessorDeclaration));

            var propertyType =
                propertyDeclarationSyntax.Type.GetTypeDefinition(context);

            if (propertyType != null) {
                propertyList.Add(new PropertyInfoModel(propertyType,
                    propertyDeclarationSyntax.Identifier.ToString(),
                    setter == null
                ));
            }
        }

        return propertyList;
    }

    private List<ParameterInfoModel> GetConstructorParameters(GeneratorSyntaxContext context) {
        var list = new List<ParameterInfoModel>();
        var constructors =
            context.Node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToList();

        constructors.Sort(
            (a, b) =>
                a.ParameterList.Parameters.Count.CompareTo(b.ParameterList.Parameters.Count));

        if (constructors.Count > 0) {
            foreach (var parameterSyntax in constructors[0].ParameterList.Parameters) {
                list.Add(new ParameterInfoModel(
                    parameterSyntax.Identifier.ToString(),
                    parameterSyntax.Type?.GetTypeDefinition(context) ?? TypeDefinition.Get(typeof(object))
                ));
            }
        }

        return list;
    }

    private bool GetEqualsFlag(GeneratorSyntaxContext context) {
        return context.Node.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ToString().Equals("Equals"));
    }

    private (bool onlyRealm, RegistrationType? registrationType, bool? generateAttribute) GetDependencyFlags(GeneratorSyntaxContext context) {
        var onlyRealm = false;
        RegistrationType? registrationType = null;
        bool? generateAttribute = null;
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            var module = classDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(attr => attr.Name.ToString().StartsWith("DependencyModule"));

            if (module is { ArgumentList: not null }) {
                foreach (var argumentSyntax in module.ArgumentList.Arguments) {
                    var name = argumentSyntax.NameEquals?.Name.ToString();

                    switch (name) {
                        case "OnlyRealm":
                            onlyRealm = argumentSyntax.Expression.ToString() == "true";
                            break;
                        case "With":
                            registrationType = GetRegistrationType(argumentSyntax.Expression.ToString());
                            break;
                        case "GenerateAttribute":
                            generateAttribute = argumentSyntax.Expression.ToString() == "true";
                            break;
                    }
                }
            }
        }
        
        return (onlyRealm, registrationType, generateAttribute);
    }
    
    public static RegistrationType GetRegistrationType(string toString) {
        var typeString = toString.Replace("RegistrationType.", "");

        if (string.IsNullOrEmpty(typeString)) {
            return RegistrationType.Add;
        }
        
        switch (typeString) {
            case "Add":
                return RegistrationType.Add;
            case "Try":
                return RegistrationType.Try;
            case "TryEnumerable":
                return  RegistrationType.TryEnumerable;
            case "Replace":
                return RegistrationType.Replace;
            default:
                throw new Exception("Unsupported RegistrationType: " + typeString);
        }
    }
}
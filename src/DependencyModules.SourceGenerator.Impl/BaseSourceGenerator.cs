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
            bool registerSourceGenerator = false;
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegistrationType", out var value)) {
                defaultRegistrationType = GetRegistrationType(value);
            }
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegisterGenerator", out var generator)) {
                registerSourceGenerator = generator.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            
            return new DependencyModuleConfigurationModel(defaultRegistrationType, registerSourceGenerator);
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
        var featureTypes = new List<ITypeDefinition>();
        List<AttributeModel>? attributes = null;

        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            attributes = AttributeModelHelper
                .GetAttributes(context, classDeclarationSyntax.AttributeLists, cancellation)
                .ToList();

            if (classDeclarationSyntax.BaseList != null) {
                foreach (var baseType in classDeclarationSyntax.BaseList.Types) {
                    var typeDefinition = baseType.Type.GetTypeDefinition(context);

                    if (typeDefinition is GenericTypeDefinition genericTypeDefinition &&
                        genericTypeDefinition.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition && 
                        genericTypeDefinition.Name == "IDependencyModuleFeature") {
                        featureTypes.Add(genericTypeDefinition.TypeArguments.First());
                    }
                }
            }
        }

        var dependencyFlags = GetDependencyFlags(context);
        var implementsEqualsFlag = GetEqualsFlag(context);
        var modelInfo = AttributeModelHelper.GetAttributeClassInfo(context, cancellation);

        return new ModuleEntryPointModel(
            context.Node.SyntaxTree?.FilePath ?? "",
            ((ClassDeclarationSyntax)context.Node).GetTypeDefinition(),
            dependencyFlags.OnlyRealm,
            dependencyFlags.RegistrationType,
            dependencyFlags.GenerateAttribute,
            dependencyFlags.RegisterGenerator,
            dependencyFlags.UseMethod,
            modelInfo.ConstructorParameters,
            implementsEqualsFlag,
            modelInfo.Properties,
            (IReadOnlyList<AttributeModel>?)attributes ?? Array.Empty<AttributeModel>(),
            featureTypes
            );
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
                    setter == null,
                    propertyDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                ));
            }
        }

        return propertyList;
    }

    private bool GetEqualsFlag(GeneratorSyntaxContext context) {
        return context.Node.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ToString().Equals("Equals"));
    }

    private record DependencyFlags
        (bool OnlyRealm, RegistrationType? RegistrationType, bool? GenerateAttribute, bool? RegisterGenerator, string? UseMethod);
    
    private DependencyFlags
        GetDependencyFlags(GeneratorSyntaxContext context) {
        var onlyRealm = false;
        RegistrationType? registrationType = null;
        bool? generateAttribute = null;
        bool? registerGenerator = null;
        string? useMethod = null;
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
                            generateAttribute = argumentSyntax.Expression.ToString().Trim('"') == "true";
                            break;
                        case "RegisterJsonSerializers":
                            registerGenerator = argumentSyntax.Expression.ToString().Trim('"') == "true";
                            break;
                        case "UseMethod":
                            useMethod = argumentSyntax.Expression.ToString().Trim('"');
                            break;
                    }
                }
            }
        }
        
        return new DependencyFlags(onlyRealm, registrationType, generateAttribute, registerGenerator, useMethod);
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
                return RegistrationType.Add;
        }
    }
}
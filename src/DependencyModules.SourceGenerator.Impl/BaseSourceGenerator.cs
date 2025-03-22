using System.Collections.Immutable;
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

        SetupRootGenerator(context, valuesProvider.Collect());
    }

    protected abstract IEnumerable<ISourceGenerator> AttributeSourceGenerators();

    private IncrementalValueProvider<DependencyModuleConfigurationModel> CreateConfigurationValueProvider(IncrementalGeneratorInitializationContext context) {
        return context.AnalyzerConfigOptionsProvider.Select((options, _) => {
            RegistrationType defaultRegistrationType = RegistrationType.Add;
            bool registerSourceGenerator = false;
            bool autoGenerateEntry = true;
            var rootNamespace = "";
            var projectDirectory = "";
            
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegistrationType", out var value)) {
                defaultRegistrationType = GetRegistrationType(value);
            }
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegisterGenerator", out var generator)) {
                registerSourceGenerator = generator.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            if (options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespaceString)) {
                rootNamespace = rootNamespaceString;
            }

            if (options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDirString)) {
                projectDirectory = projectDirString;
            }
            
            if (options.GlobalOptions.TryGetValue("build_property.DependencyModules_AutoGenerateModule", out var autoGenerateEntryString)) {
                autoGenerateEntry = autoGenerateEntryString.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            
            return new DependencyModuleConfigurationModel(
                defaultRegistrationType, 
                registerSourceGenerator, 
                rootNamespace,
                projectDirectory,
                autoGenerateEntry);
        }).WithComparer(new DependencyModuleConfigurationModelComparer());
    }

    protected virtual void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) { }

    private IncrementalValuesProvider<ModuleEntryPointModel> CreateSourceValueProvider(IncrementalGeneratorInitializationContext context) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax, CompilationUnitSyntax>(
            KnownTypes.DependencyModules.Attributes.DependencyModuleAttribute) {
            AutoApproveCompilationUnit = true,
            ApproveFilter = "Program.cs",
        };

        return context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateEntryPointModel
        ).WithComparer(new ModuleEntryPointModelComparer());
    }

    protected virtual ModuleEntryPointModel GenerateEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();

        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            return GetClassEntryPointModel(context, cancellation, classDeclarationSyntax);
        }

        return GetCompilationUnitSyntaxEntry(context, cancellation);
    }

    private ModuleEntryPointModel GetClassEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation, ClassDeclarationSyntax classDeclarationSyntax) {
        var featureTypes = new List<ITypeDefinition>();
        ModuleEntryPointFeatures features = ModuleEntryPointFeatures.None;
        List<AttributeModel>? attributes = AttributeModelHelper
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
        
        var dependencyFlags = GetDependencyFlags(context);
        var implementsEqualsFlag = GetEqualsFlag(context);
        var modelInfo = AttributeModelHelper.GetAttributeClassInfo(context, cancellation);

        if (dependencyFlags.OnlyRealm) {
            features |= ModuleEntryPointFeatures.OnlyRealm;
        }

        if (!implementsEqualsFlag) {
            features |= ModuleEntryPointFeatures.ShouldImplementEquals;
        }
        
        return new ModuleEntryPointModel(
            features,
            context.Node.SyntaxTree?.FilePath ?? "",
            ((ClassDeclarationSyntax)context.Node).GetTypeDefinition(),
            dependencyFlags.RegistrationType,
            dependencyFlags.GenerateAttribute,
            dependencyFlags.RegisterGenerator,
            dependencyFlags.UseMethod,
            modelInfo.ConstructorParameters,
            modelInfo.Properties,
            (IReadOnlyList<AttributeModel>?)attributes ?? Array.Empty<AttributeModel>(),
            Array.Empty<ITypeDefinition>(),
            featureTypes
        );
    }

    private ModuleEntryPointModel GetCompilationUnitSyntaxEntry(GeneratorSyntaxContext context, CancellationToken cancellation) {
        var compilationUnitSyntax = (CompilationUnitSyntax)context.Node;
        var attributes = AttributeModelHelper
            .GetAttributes(context, compilationUnitSyntax.AttributeLists, cancellation)
            .ToList();
        var additionalModules = new List<ITypeDefinition>();
        
        foreach (var syntax in compilationUnitSyntax.Members) {
            if (syntax is GlobalStatementSyntax globalStatementSyntax) {
                if (globalStatementSyntax.Statement is ExpressionStatementSyntax expressionStatementSyntax) {
                    if (expressionStatementSyntax.Expression is InvocationExpressionSyntax invocationExpressionSyntax) {

                        if (context.SemanticModel.GetSymbolInfo(expressionStatementSyntax.Expression).Symbol
                            is IMethodSymbol { IsStatic: true } methodSymbol) {
                            
                            var typeSymbol = methodSymbol.ContainingSymbol as ITypeSymbol;
                            var declaringType = methodSymbol.ContainingType;
                            var moduleInterface = typeSymbol?.AllInterfaces.Any(x => x.GetTypeDefinition().Equals(KnownTypes.DependencyModules.Interfaces.IDependencyModule));

                            if (moduleInterface.GetValueOrDefault(false) &&
                                declaringType.Constructors.Any(c => c.Parameters.Length == 0)) {
                                additionalModules.Add(declaringType.GetTypeDefinition());
                            }
                        }
                    }
                }
            }
        }
        
        return new ModuleEntryPointModel(
            ModuleEntryPointFeatures.AutoGenerateModule,
            context.Node.SyntaxTree?.FilePath ?? "",
            TypeDefinition.Get("", "ApplicationModule"),
            null,
            true,
            false,
            null,
            new ParameterInfoModel[0],
            Array.Empty<PropertyInfoModel>(),
            (IReadOnlyList<AttributeModel>?)attributes ?? Array.Empty<AttributeModel>(),
            additionalModules,
            Array.Empty<ITypeDefinition>()
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
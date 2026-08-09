using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
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

    protected abstract IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators();

    /// <summary>
    /// Returns the attribute types that trigger module source generation.
    /// Override to support custom trigger attributes (e.g. for framework-specific module attributes).
    /// </summary>
    protected virtual ITypeDefinition[] ModuleAttributeTypes() {
        return new[] { KnownTypes.DependencyModules.Attributes.DependencyModuleAttribute };
    }

    private IncrementalValueProvider<DependencyModuleConfigurationModel> CreateConfigurationValueProvider(IncrementalGeneratorInitializationContext context) {
        return context.AnalyzerConfigOptionsProvider.Select((options, _) => {
            RegistrationType defaultRegistrationType = RegistrationType.Add;
            bool registerSourceGenerator = false;
            bool autoGenerateEntry = true;
            bool generateFactories = false;
            var rootNamespace = "";
            var projectDirectory = "";
            var logOutputFolder = "";
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_RegistrationType", out var value)) {
                defaultRegistrationType = GetRegistrationType(value);
            }
            
            if (options.GlobalOptions.TryGetValue(
                    "build_property.DependencyModules_LogOutputDirectory", out var logOutputFolderValue)) {
                logOutputFolder = logOutputFolderValue;
            }
            
            if (TryGetBoolean(options, "DependencyModules_RegisterGenerator", out var generator)) {
                registerSourceGenerator = generator;
            }

            if (options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespaceString)) {
                rootNamespace = rootNamespaceString;
            }

            if (options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDirString)) {
                projectDirectory = projectDirString;
            }
            
            if (TryGetBoolean(options, "DependencyModules_AutoGenerateModule", out var autoGenerateEntryValue)) {
                autoGenerateEntry = autoGenerateEntryValue;
            }
            
            if (TryGetBoolean(options, "DependencyModules_GenerateFactories", out var generateFactoriesValue)) {
                generateFactories = generateFactoriesValue;
            }

            var excludeGeneratedCodeFromCoverage = true;
            if (TryGetBoolean(options, "ExcludeGeneratedCodeFromCoverage", out var excludeCoverageValue)) {
                excludeGeneratedCodeFromCoverage = excludeCoverageValue;
            }

            return new DependencyModuleConfigurationModel(
                defaultRegistrationType,
                registerSourceGenerator,
                rootNamespace,
                projectDirectory,
                autoGenerateEntry,
                logOutputFolder,
                LogOutputLevel.Debug,
                generateFactories,
                excludeGeneratedCodeFromCoverage);
        }).WithComparer(new DependencyModuleConfigurationModelComparer());
    }

    /// <summary>
    /// Reads a boolean build property, treating an unset or blank value as "not specified".
    /// </summary>
    /// <remarks>
    /// A property listed as CompilerVisibleProperty is always delivered, as an empty string when
    /// the developer has not set it. Without this check every boolean default would be overwritten
    /// with false the moment the property was made visible.
    /// </remarks>
    private static bool TryGetBoolean(AnalyzerConfigOptionsProvider options, string propertyName, out bool value) {
        value = false;

        if (!options.GlobalOptions.TryGetValue("build_property." + propertyName, out var raw) ||
            string.IsNullOrWhiteSpace(raw)) {
            return false;
        }

        value = raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        return true;
    }

    protected virtual void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) { }

    protected virtual bool ShouldAutoApproveCompilationUnit => true;

    private IncrementalValuesProvider<ModuleEntryPointModel> CreateSourceValueProvider(IncrementalGeneratorInitializationContext context) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax, RecordDeclarationSyntax, CompilationUnitSyntax>(
            ModuleAttributeTypes()) {
            AutoApproveCompilationUnit = ShouldAutoApproveCompilationUnit,
            ApproveFilter = "Program.cs",
        };

        return context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateEntryPointModel
        ).WithComparer(new ModuleEntryPointModelComparer());
    }

    protected virtual ModuleEntryPointModel GenerateEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();

        if (context.Node is TypeDeclarationSyntax typeDeclarationSyntax) {
            return GetClassEntryPointModel(context, cancellation, typeDeclarationSyntax);
        }

        return GetCompilationUnitSyntaxEntry(context, cancellation);
    }

    private ModuleEntryPointModel GetClassEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation, TypeDeclarationSyntax typeDeclarationSyntax) {
        var featureTypes = new List<ITypeDefinition>();
        ModuleEntryPointFeatures features = ModuleEntryPointFeatures.None;
        List<AttributeModel>? attributes = AttributeModelHelper
            .GetAttributes(context, typeDeclarationSyntax.AttributeLists, cancellation)
            .ToList();

        if (typeDeclarationSyntax.BaseList != null) {
            foreach (var baseType in typeDeclarationSyntax.BaseList.Types) {
                var typeDefinition = baseType.Type.GetTypeDefinition(context);

                if (typeDefinition is GenericTypeDefinition { TypeDefinitionEnum: TypeDefinitionEnum.InterfaceDefinition, Name: "IDependencyModuleFeature" } genericTypeDefinition) {
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

        if (typeDeclarationSyntax is RecordDeclarationSyntax) {
            features |= ModuleEntryPointFeatures.IsRecord;
        }
        else if (!implementsEqualsFlag) {
            features |= ModuleEntryPointFeatures.ShouldImplementEquals;
        }

        if (!typeDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) {
            features |= ModuleEntryPointFeatures.NotPartial;
        }
        
        return new ModuleEntryPointModel(
            features,
            context.Node.SyntaxTree?.FilePath ?? "",
            ((TypeDeclarationSyntax)context.Node).GetTypeDefinition(),
            dependencyFlags.RegistrationType,
            dependencyFlags.GenerateAttribute,
            dependencyFlags.RegisterGenerator,
            dependencyFlags.UseMethod,
            dependencyFlags.GenerateFactories,
            modelInfo.ConstructorInfo.Parameters,
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
            if (syntax is GlobalStatementSyntax { Statement: ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocationExpressionSyntax } expressionStatementSyntax }) {

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
        
        return new ModuleEntryPointModel(
            ModuleEntryPointFeatures.AutoGenerateModule,
            context.Node.SyntaxTree?.FilePath ?? "",
            TypeDefinition.Get("", "ApplicationModule"),
            null,
            true,
            false,
            null,
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
        (bool OnlyRealm, RegistrationType? RegistrationType, bool? GenerateAttribute, bool? GenerateFactories, bool? RegisterGenerator, string? UseMethod);
    
    private DependencyFlags
        GetDependencyFlags(GeneratorSyntaxContext context) {
        var onlyRealm = false;
        RegistrationType? registrationType = null;
        bool? generateAttribute = null;
        bool? registerGenerator = null;
        bool? generateFactories = null;
        string? useMethod = null;
        if (context.Node is TypeDeclarationSyntax typeDeclarationSyntax) {
            var module = typeDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(attr => attr.Name.ToString().StartsWith("DependencyModule"));

            if (module is { ArgumentList: not null }) {
                foreach (var argumentSyntax in module.ArgumentList.Arguments) {
                    var name = argumentSyntax.NameEquals?.Name.ToString();

                    switch (name) {
                        case "OnlyRealm":
                            onlyRealm = argumentSyntax.Expression.ToString() == "true";
                            break;
                        case "Using":
                            registrationType = GetRegistrationType(argumentSyntax.Expression.ToString());
                            break;
                        case "GenerateAttribute":
                            generateAttribute = argumentSyntax.Expression.ToString().Trim('"') == "true";
                            break;
                        case "RegisterJsonSerializers":
                            registerGenerator = argumentSyntax.Expression.ToString().Trim('"') == "true";
                            break;
                        case "GenerateUseMethod":
                            useMethod = argumentSyntax.Expression.ToString().Trim('"');
                            break;
                        case "GenerateFactories":
                            generateFactories = argumentSyntax.Expression.ToString().Trim('"') == "true";
                            break;
                    }
                }
            }
        }
        
        return new DependencyFlags(
            onlyRealm,
            registrationType, 
            generateAttribute,
            generateFactories,
            registerGenerator, 
            useMethod);
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
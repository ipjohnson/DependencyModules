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

            var codeStyle = BraceStyle.Allman;
            if (options.GlobalOptions.TryGetValue("build_property.GeneratedCodeStyle", out var codeStyleValue)) {
                codeStyle = GetCodeStyle(codeStyleValue);
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
                excludeGeneratedCodeFromCoverage,
                codeStyle);
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

    /// <summary>
    /// Writes the module partial for every type carrying one of <see cref="ModuleAttributeTypes"/>,
    /// unless those are modules another generator already writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two shapes a generator takes here want opposite things, and this used to be empty for
    /// both. A framework naming its own module attribute is the only generator that can write those
    /// modules, and forgetting to override this compiled cleanly, emitted no module, and failed at
    /// the consumer's <c>AddModule&lt;T&gt;()</c> — a generic constraint error naming neither the
    /// generator nor the omission. It now writes them by default.
    /// </para>
    /// <para>
    /// A generator triggering on <c>[DependencyModule]</c> instead is adding registrations to
    /// modules this package's own generator already writes, so it emits nothing; writing them from
    /// both would declare every module twice. That generator opts in the way the shipped one does,
    /// by overriding this.
    /// </para>
    /// <para>
    /// A framework that declares its own module attribute and wants no module written for it — one
    /// contributing only providers — overrides this with an empty body.
    /// </para>
    /// </remarks>
    protected virtual void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) {

        if (TriggersOnDefaultModuleAttribute()) {
            return;
        }

        DependencyModuleWriter.Register(context, valuesProvider, generateAttribute: true);
    }

    /// <summary>
    /// Whether <c>Program.cs</c> is an entry point on its own, so that an application written with
    /// top level statements gets a generated <c>ApplicationModule</c> without declaring one.
    /// </summary>
    /// <remarks>
    /// True only while triggering on <c>[DependencyModule]</c>, which is what an
    /// <c>ApplicationModule</c> stands in for. A compilation unit carries no module attribute to
    /// distinguish by, so every generator derived from this class claimed the same
    /// <c>Program.cs</c> — and a framework generator loaded alongside this package's own emitted a
    /// second <c>ApplicationModule</c>, with the same members declared twice. A framework that
    /// ships without this package's generator, and wants the top level statement module for itself,
    /// overrides this to true.
    /// </remarks>
    protected virtual bool ShouldAutoApproveCompilationUnit => TriggersOnDefaultModuleAttribute();

    /// <summary>
    /// Whether this generator reads the module attribute this package declares, rather than one of
    /// its own. It decides who writes a module, and who is contributing to someone else's.
    /// </summary>
    private bool TriggersOnDefaultModuleAttribute() {
        var moduleAttributes = ModuleAttributeTypes();

        return moduleAttributes.Length == 1 &&
               moduleAttributes[0].Equals(KnownTypes.DependencyModules.Attributes.DependencyModuleAttribute);
    }

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

        // A module nested inside another type cannot be completed where it was written: the
        // generated half is emitted at namespace level, so it becomes a second, unrelated type and
        // the nested declaration never implements IDependencyModule.
        if (typeDeclarationSyntax.Parent is TypeDeclarationSyntax) {
            features |= ModuleEntryPointFeatures.NestedInType;
        }
        
        return new ModuleEntryPointModel(
            features,
            context.Node.SyntaxTree?.FilePath ?? "",
            LocationModel.From(context.Node),
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
            LocationModel.From(context.Node),
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
    
    /// <summary>
    /// Parses the GeneratedCodeStyle build property. The name carries no framework prefix on
    /// purpose: it is shared with other source generators, so one csproj line styles all of them.
    /// </summary>
    public static BraceStyle GetCodeStyle(string value) {
        switch (value.Trim().ToLowerInvariant()) {
            case "kandr":
            case "k&r":
                return BraceStyle.KAndR;
            default:
                return BraceStyle.Allman;
        }
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
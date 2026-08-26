using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// Builds generator model objects for tests. The model records have wide positional constructors,
/// so tests name only the field they care about and take defaults for the rest.
/// </summary>
public static class ModelFactory {

    public static ModuleEntryPointModel EntryPoint(
        ModuleEntryPointFeatures features = ModuleEntryPointFeatures.None,
        string fileLocation = "/project/Module.cs",
        ITypeDefinition? entryPointType = null,
        RegistrationType? registrationType = null,
        bool? generateAttribute = null,
        bool? registerJsonSerializers = null,
        string? useMethod = null,
        bool? generateFactories = null,
        IReadOnlyList<ParameterInfoModel>? parameters = null,
        IReadOnlyList<PropertyInfoModel>? properties = null,
        IReadOnlyList<AttributeModel>? attributes = null,
        IReadOnlyList<ITypeDefinition>? additionalModules = null,
        IReadOnlyList<ITypeDefinition>? featureTypes = null) =>
        new(
            features,
            fileLocation,
            LocationModel.None,
            entryPointType ?? TypeDefinition.Get("TestNamespace", "TestModule"),
            registrationType,
            generateAttribute,
            registerJsonSerializers,
            useMethod,
            generateFactories,
            parameters ?? new List<ParameterInfoModel>(),
            properties ?? new List<PropertyInfoModel>(),
            attributes ?? new List<AttributeModel>(),
            additionalModules ?? new List<ITypeDefinition>(),
            featureTypes ?? new List<ITypeDefinition>());

    public static DependencyModuleConfigurationModel Configuration(
        RegistrationType registrationType = RegistrationType.Add,
        bool registerSourceGenerator = false,
        string rootNamespace = "TestNamespace",
        string projectDir = "/project/",
        bool autoGenerateEntry = true,
        string logOutputFolder = "",
        LogOutputLevel logOutputLevel = LogOutputLevel.Debug,
        bool generateFactories = false,
        bool excludeGeneratedCodeFromCoverage = true) =>
        new(
            registrationType,
            registerSourceGenerator,
            rootNamespace,
            projectDir,
            autoGenerateEntry,
            logOutputFolder,
            logOutputLevel,
            generateFactories,
            excludeGeneratedCodeFromCoverage);
}

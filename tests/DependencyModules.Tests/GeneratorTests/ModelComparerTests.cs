using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// These comparers are what Roslyn consults to decide whether a pipeline step's output changed.
/// Every field that affects generated code must make two models compare unequal, or an edit to
/// that field will serve stale output.
/// </summary>
public class ModelComparerTests {

    private readonly ModuleEntryPointModelComparer _entryPointComparer = new();
    private readonly DependencyModuleConfigurationModelComparer _configurationComparer = new();

    [Fact]
    public void EntryPoints_BuiltFromTheSameValues_AreEqual() {
        Assert.True(_entryPointComparer.Equals(ModelFactory.EntryPoint(), ModelFactory.EntryPoint()));
    }

    [Fact]
    public void EqualEntryPoints_ShareAHashCode() {
        Assert.Equal(
            _entryPointComparer.GetHashCode(ModelFactory.EntryPoint()),
            _entryPointComparer.GetHashCode(ModelFactory.EntryPoint()));
    }

    [Fact]
    public void EntryPoints_BothNull_AreEqual() {
        Assert.True(_entryPointComparer.Equals(null, null));
    }

    [Fact]
    public void EntryPoints_OneNull_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(ModelFactory.EntryPoint(), null));
        Assert.False(_entryPointComparer.Equals(null, ModelFactory.EntryPoint()));
    }

    [Fact]
    public void EntryPoints_DifferingByFileLocation_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(fileLocation: "/project/A.cs"),
            ModelFactory.EntryPoint(fileLocation: "/project/B.cs")));
    }

    [Fact]
    public void EntryPoints_DifferingByType_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(entryPointType: TypeDefinition.Get("Ns", "One")),
            ModelFactory.EntryPoint(entryPointType: TypeDefinition.Get("Ns", "Two"))));
    }

    [Fact]
    public void EntryPoints_DifferingByFeatures_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(features: ModuleEntryPointFeatures.None),
            ModelFactory.EntryPoint(features: ModuleEntryPointFeatures.OnlyRealm)));
    }

    [Fact]
    public void EntryPoints_DifferingByUseMethod_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(useMethod: "UseOne"),
            ModelFactory.EntryPoint(useMethod: "UseTwo")));
    }

    [Fact]
    public void EntryPoints_DifferingByRegistrationType_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(registrationType: RegistrationType.Add),
            ModelFactory.EntryPoint(registrationType: RegistrationType.Try)));
    }

    [Fact]
    public void EntryPoints_DifferingByGenerateAttribute_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(generateAttribute: true),
            ModelFactory.EntryPoint(generateAttribute: false)));
    }

    [Fact]
    public void EntryPoints_DifferingByJsonSerializerRegistration_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(registerJsonSerializers: true),
            ModelFactory.EntryPoint(registerJsonSerializers: false)));
    }

    [Fact]
    public void EntryPoints_DifferingByFactoryGeneration_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(generateFactories: true),
            ModelFactory.EntryPoint(generateFactories: false)));
    }

    [Fact]
    public void EntryPoints_DifferingByParameters_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(parameters: [Parameter("one")]),
            ModelFactory.EntryPoint(parameters: [Parameter("two")])));
    }

    [Fact]
    public void EntryPoints_WithSeparateButEqualParameters_AreEqual() {
        Assert.True(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(parameters: [Parameter("same")]),
            ModelFactory.EntryPoint(parameters: [Parameter("same")])));
    }

    [Fact]
    public void EntryPoints_DifferingByProperties_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(properties: [Property("One")]),
            ModelFactory.EntryPoint(properties: [Property("Two")])));
    }

    [Fact]
    public void EntryPoints_DifferingByAttributes_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(attributes: [Attribute("One")]),
            ModelFactory.EntryPoint(attributes: [Attribute("Two")])));
    }

    [Fact]
    public void EntryPoints_WithSeparateButEqualAttributes_AreEqual() {
        Assert.True(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(attributes: [Attribute("Same")]),
            ModelFactory.EntryPoint(attributes: [Attribute("Same")])));
    }

    [Fact]
    public void EntryPoints_DifferingByFeatureTypes_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(featureTypes: [TypeDefinition.Get("Ns", "IOne")]),
            ModelFactory.EntryPoint(featureTypes: [TypeDefinition.Get("Ns", "ITwo")])));
    }

    [Fact]
    public void EntryPoints_DifferingByAdditionalModules_AreNotEqual() {
        Assert.False(_entryPointComparer.Equals(
            ModelFactory.EntryPoint(additionalModules: [TypeDefinition.Get("Ns", "One")]),
            ModelFactory.EntryPoint(additionalModules: [TypeDefinition.Get("Ns", "Two")])));
    }

    [Fact]
    public void Configurations_BuiltFromTheSameValues_AreEqual() {
        Assert.True(_configurationComparer.Equals(ModelFactory.Configuration(), ModelFactory.Configuration()));
    }

    [Fact]
    public void SameConfigurationInstance_IsEqualToItself() {
        var configuration = ModelFactory.Configuration();

        Assert.True(_configurationComparer.Equals(configuration, configuration));
    }

    [Fact]
    public void Configurations_OneNull_AreNotEqual() {
        Assert.False(_configurationComparer.Equals(ModelFactory.Configuration(), null));
        Assert.False(_configurationComparer.Equals(null, ModelFactory.Configuration()));
    }

    [Fact]
    public void EqualConfigurations_ShareAHashCode() {
        Assert.Equal(
            _configurationComparer.GetHashCode(ModelFactory.Configuration()),
            _configurationComparer.GetHashCode(ModelFactory.Configuration()));
    }

    [Theory]
    [MemberData(nameof(DifferingConfigurations))]
    public void Configurations_DifferingByAnyField_AreNotEqual(
        string field, DependencyModuleConfigurationModel other) {

        Assert.False(
            _configurationComparer.Equals(ModelFactory.Configuration(), other),
            $"Configurations differing by {field} compared equal, so a change to it would serve stale output.");
    }

    public static TheoryData<string, DependencyModuleConfigurationModel> DifferingConfigurations() =>
        new() {
            { nameof(DependencyModuleConfigurationModel.RegistrationType), ModelFactory.Configuration(registrationType: RegistrationType.Try) },
            { nameof(DependencyModuleConfigurationModel.RegisterSourceGenerator), ModelFactory.Configuration(registerSourceGenerator: true) },
            { nameof(DependencyModuleConfigurationModel.RootNamespace), ModelFactory.Configuration(rootNamespace: "Other") },
            { nameof(DependencyModuleConfigurationModel.ProjectDir), ModelFactory.Configuration(projectDir: "/other/") },
            { nameof(DependencyModuleConfigurationModel.AutoGenerateEntry), ModelFactory.Configuration(autoGenerateEntry: false) },
            { nameof(DependencyModuleConfigurationModel.LogOutputFolder), ModelFactory.Configuration(logOutputFolder: "/logs") },
            { nameof(DependencyModuleConfigurationModel.LogOutputLevel), ModelFactory.Configuration(logOutputLevel: LogOutputLevel.Error) },
            { nameof(DependencyModuleConfigurationModel.GenerateFactories), ModelFactory.Configuration(generateFactories: true) },
            { nameof(DependencyModuleConfigurationModel.ExcludeGeneratedCodeFromCoverage), ModelFactory.Configuration(excludeGeneratedCodeFromCoverage: false) }
        };

    private static ParameterInfoModel Parameter(string name) =>
        new(name, TypeDefinition.Get("Ns", "SomeType"), null, []);

    private static PropertyInfoModel Property(string name) =>
        new(TypeDefinition.Get("Ns", "SomeType"), name, false, false);

    private static AttributeModel Attribute(string name) =>
        new(TypeDefinition.Get("Ns", name), [], [], []);
}

using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// These comparers are what Roslyn consults to decide whether a pipeline step's output changed.
/// Every field that affects generated code must make two models compare unequal, or an edit to
/// that field will serve stale output.
/// </summary>
public partial class ModelComparerTests
{
    private readonly ModuleEntryPointModelComparer _entryPointComparer = new();
    private readonly DependencyModuleConfigurationModelComparer _configurationComparer = new();

    [VerifyTest]
    public void EntryPoints_BuiltFromTheSameValues_AreEqual()
    {
        Verify(_entryPointComparer.Equals(ModelFactory.EntryPoint(), ModelFactory.EntryPoint()));
    }

    [VerifyTest]
    public void EqualEntryPoints_ShareAHashCode()
    {
        Verify(
            _entryPointComparer.GetHashCode(ModelFactory.EntryPoint())
                == _entryPointComparer.GetHashCode(ModelFactory.EntryPoint())
        );
    }

    [VerifyTest]
    public void EntryPoints_BothNull_AreEqual()
    {
        Verify(_entryPointComparer.Equals(null, null));
    }

    [VerifyTest]
    public void EntryPoints_OneNull_AreNotEqual()
    {
        Verify(!_entryPointComparer.Equals(ModelFactory.EntryPoint(), null));
        Verify(!_entryPointComparer.Equals(null, ModelFactory.EntryPoint()));
    }

    [VerifyTest]
    public void EntryPoints_DifferingByFileLocation_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(fileLocation: "/project/A.cs"),
                ModelFactory.EntryPoint(fileLocation: "/project/B.cs")
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByType_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(entryPointType: TypeDefinition.Get("Ns", "One")),
                ModelFactory.EntryPoint(entryPointType: TypeDefinition.Get("Ns", "Two"))
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByFeatures_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(features: ModuleEntryPointFeatures.None),
                ModelFactory.EntryPoint(features: ModuleEntryPointFeatures.OnlyRealm)
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByUseMethod_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(useMethod: "UseOne"),
                ModelFactory.EntryPoint(useMethod: "UseTwo")
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByRegistrationType_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(registrationType: RegistrationType.Add),
                ModelFactory.EntryPoint(registrationType: RegistrationType.Try)
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByGenerateAttribute_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(generateAttribute: true),
                ModelFactory.EntryPoint(generateAttribute: false)
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByJsonSerializerRegistration_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(registerJsonSerializers: true),
                ModelFactory.EntryPoint(registerJsonSerializers: false)
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByFactoryGeneration_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(generateFactories: true),
                ModelFactory.EntryPoint(generateFactories: false)
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByParameters_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(parameters: [Parameter("one")]),
                ModelFactory.EntryPoint(parameters: [Parameter("two")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_WithSeparateButEqualParameters_AreEqual()
    {
        Verify(
            _entryPointComparer.Equals(
                ModelFactory.EntryPoint(parameters: [Parameter("same")]),
                ModelFactory.EntryPoint(parameters: [Parameter("same")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByProperties_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(properties: [Property("One")]),
                ModelFactory.EntryPoint(properties: [Property("Two")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByAttributes_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(attributes: [Attribute("One")]),
                ModelFactory.EntryPoint(attributes: [Attribute("Two")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_WithSeparateButEqualAttributes_AreEqual()
    {
        Verify(
            _entryPointComparer.Equals(
                ModelFactory.EntryPoint(attributes: [Attribute("Same")]),
                ModelFactory.EntryPoint(attributes: [Attribute("Same")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByFeatureTypes_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(featureTypes: [TypeDefinition.Get("Ns", "IOne")]),
                ModelFactory.EntryPoint(featureTypes: [TypeDefinition.Get("Ns", "ITwo")])
            )
        );
    }

    [VerifyTest]
    public void EntryPoints_DifferingByAdditionalModules_AreNotEqual()
    {
        Verify(
            !_entryPointComparer.Equals(
                ModelFactory.EntryPoint(additionalModules: [TypeDefinition.Get("Ns", "One")]),
                ModelFactory.EntryPoint(additionalModules: [TypeDefinition.Get("Ns", "Two")])
            )
        );
    }

    [VerifyTest]
    public void Configurations_BuiltFromTheSameValues_AreEqual()
    {
        Verify(
            _configurationComparer.Equals(
                ModelFactory.Configuration(),
                ModelFactory.Configuration()
            )
        );
    }

    [VerifyTest]
    public void SameConfigurationInstance_IsEqualToItself()
    {
        var configuration = ModelFactory.Configuration();

        Verify(_configurationComparer.Equals(configuration, configuration));
    }

    [VerifyTest]
    public void Configurations_OneNull_AreNotEqual()
    {
        Verify(!_configurationComparer.Equals(ModelFactory.Configuration(), null));
        Verify(!_configurationComparer.Equals(null, ModelFactory.Configuration()));
    }

    [VerifyTest]
    public void EqualConfigurations_ShareAHashCode()
    {
        Verify(
            _configurationComparer.GetHashCode(ModelFactory.Configuration())
                == _configurationComparer.GetHashCode(ModelFactory.Configuration())
        );
    }

    [VerifyTest]
    [RowSource(nameof(DifferingConfigurations), DeterministicReplay = true)]
    public void Configurations_DifferingByAnyField_AreNotEqual(
        string field,
        DependencyModuleConfigurationModel other
    )
    {
        Verify(
            !_configurationComparer.Equals(ModelFactory.Configuration(), other),
            $"Configurations differing by {field} compared equal, so a change to it would serve stale output."
        );
    }

    public static IEnumerable<(
        string Field,
        DependencyModuleConfigurationModel Other
    )> DifferingConfigurations() =>
        [
            (
                nameof(DependencyModuleConfigurationModel.RegistrationType),
                ModelFactory.Configuration(registrationType: RegistrationType.Try)
            ),
            (
                nameof(DependencyModuleConfigurationModel.RegisterSourceGenerator),
                ModelFactory.Configuration(registerSourceGenerator: true)
            ),
            (
                nameof(DependencyModuleConfigurationModel.RootNamespace),
                ModelFactory.Configuration(rootNamespace: "Other")
            ),
            (
                nameof(DependencyModuleConfigurationModel.ProjectDir),
                ModelFactory.Configuration(projectDir: "/other/")
            ),
            (
                nameof(DependencyModuleConfigurationModel.AutoGenerateEntry),
                ModelFactory.Configuration(autoGenerateEntry: false)
            ),
            (
                nameof(DependencyModuleConfigurationModel.LogOutputFolder),
                ModelFactory.Configuration(logOutputFolder: "/logs")
            ),
            (
                nameof(DependencyModuleConfigurationModel.LogOutputLevel),
                ModelFactory.Configuration(logOutputLevel: LogOutputLevel.Error)
            ),
            (
                nameof(DependencyModuleConfigurationModel.GenerateFactories),
                ModelFactory.Configuration(generateFactories: true)
            ),
            (
                nameof(DependencyModuleConfigurationModel.ExcludeGeneratedCodeFromCoverage),
                ModelFactory.Configuration(excludeGeneratedCodeFromCoverage: false)
            ),
            (
                nameof(DependencyModuleConfigurationModel.GeneratedCodeStyle),
                ModelFactory.Configuration(generatedCodeStyle: BraceStyle.KAndR)
            ),
        ];

    private readonly InterceptorModelComparer _interceptorComparer = new();

    /// <summary>
    /// Realm decides which module emits an interception's applicator, and it was the one field this
    /// comparer left out — so changing only `Realm = typeof(X)` served the previous run's output and
    /// the interception stayed on whichever module had it before. Both sibling comparers already
    /// compared theirs.
    /// </summary>
    [VerifyTest]
    public void Interceptors_DifferingByRealm_AreNotEqual()
    {
        Verify(
            !_interceptorComparer.Equals(
                Interceptor(realm: TypeDefinition.Get("Ns", "OneRealm")),
                Interceptor(realm: TypeDefinition.Get("Ns", "OtherRealm"))
            )
        );
    }

    [VerifyTest]
    public void Interceptors_GainingARealm_AreNotEqual()
    {
        Verify(
            !_interceptorComparer.Equals(
                Interceptor(),
                Interceptor(realm: TypeDefinition.Get("Ns", "OneRealm"))
            )
        );
    }

    [VerifyTest]
    public void Interceptors_BuiltFromTheSameValues_AreEqual()
    {
        Verify(_interceptorComparer.Equals(Interceptor(), Interceptor()));
    }

    [VerifyTest]
    public void EqualInterceptors_ShareAHashCode()
    {
        Verify(
            _interceptorComparer.GetHashCode(Interceptor())
                == _interceptorComparer.GetHashCode(Interceptor())
        );
    }

    [VerifyTest]
    public void Interceptors_DifferingByRealm_DoNotShareAHashCode()
    {
        Verify(
            _interceptorComparer.GetHashCode(Interceptor())
                != _interceptorComparer.GetHashCode(
                    Interceptor(realm: TypeDefinition.Get("Ns", "OneRealm"))
                )
        );
    }

    [VerifyTest]
    public void Interceptors_DifferingByOrder_AreNotEqual()
    {
        Verify(!_interceptorComparer.Equals(Interceptor(order: 1), Interceptor(order: 2)));
    }

    private static InterceptorModel Interceptor(ITypeDefinition? realm = null, int order = 0) =>
        new(
            TypeDefinition.Get("Ns", "IService"),
            TypeDefinition.Get("Ns", "Service"),
            [],
            [],
            [],
            order,
            Realm: realm
        );

    private static ParameterInfoModel Parameter(string name) =>
        new(name, TypeDefinition.Get("Ns", "SomeType"), null, []);

    private static PropertyInfoModel Property(string name) =>
        new(TypeDefinition.Get("Ns", "SomeType"), name, false, false, true);

    private static AttributeModel Attribute(string name) =>
        new(TypeDefinition.Get("Ns", name), [], [], []);
}

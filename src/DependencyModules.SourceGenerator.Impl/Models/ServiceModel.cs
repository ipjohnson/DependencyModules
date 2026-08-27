using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public enum ServiceLifestyle {
    Transient,
    Scoped,
    Singleton
}

public enum RegistrationType {
    Add,
    Try,
    TryEnumerable,
    Replace
}

[Flags]
public enum RegistrationFeature {
    None= 0,
    AutoRegisterSourceGenerator = 1,

    /// <summary>
    /// The implementation is abstract. The container cannot construct it, so registering it would
    /// fail when the provider is built rather than when the code is compiled.
    /// </summary>
    AbstractImplementation = 2,

    /// <summary>
    /// The implementation is a static class, which likewise cannot be constructed.
    /// </summary>
    StaticImplementation = 4,

    /// <summary>
    /// The implementation carries <c>[Intercept]</c>, so its registration has to stay identifiable
    /// as its own.
    /// </summary>
    /// <remarks>
    /// Interception is applied by rewriting the one registration the wrapper was generated from,
    /// and finding it means asking a descriptor which implementation it was built from. A factory
    /// descriptor cannot answer, so under <c>DependencyModules_GenerateFactories</c> the filter
    /// matched nothing and interception went back to wrapping every registration of the service
    /// type - the exact behaviour 1.1.0 shipped to fix, restored by the property the AOT guidance
    /// recommends turning on.
    ///
    /// So an intercepted implementation keeps its <c>typeof</c> registration whatever the property
    /// says. It costs that one service the property's benefit and nothing else: the wrapper around
    /// it is still emitted as a literal <c>new</c>, and a <c>typeof</c> registration is the default
    /// shape everywhere else, trimmer-annotated and already proven under Native AOT.
    /// </remarks>
    Intercepted = 8,
}

public record ServiceFactoryModel(
    ITypeDefinition TypeDefinition,
    string MethodName,
    IReadOnlyList<ParameterInfoModel> Parameters) {

    // Structural equality over Parameters; see ModelEquality.
    public virtual bool Equals(ServiceFactoryModel? other) =>
        other is not null &&
        TypeDefinition.Equals(other.TypeDefinition) &&
        MethodName == other.MethodName &&
        ModelEquality.ListEquals(Parameters, other.Parameters);

    public override int GetHashCode() {
        unchecked {
            var hash = TypeDefinition.GetHashCode();
            hash = hash * 31 + MethodName.GetHashCode();
            hash = hash * 31 + ModelEquality.ListHashCode(Parameters);
            return hash;
        }
    }
}

public record ServiceRegistrationModel(
    ITypeDefinition ServiceType,
    ServiceLifestyle Lifestyle,
    RegistrationType? RegistrationType = null,
    ITypeDefinition? Realm = null,
    object? Key = null,
    bool? CrossWire = false,
    IReadOnlyList<string>? Namespaces = null,

    /// <summary>
    /// Where this registration sits among the others for the same service, lowest first. Decides the
    /// sequence an <c>IEnumerable&lt;T&gt;</c> dependency sees, and therefore which one a single
    /// resolve returns.
    /// </summary>
    int Order = 0);

public delegate IOutputComponent? FactoryOutputDelegate(
    ServiceModel serviceModel, ServiceRegistrationModel registrationModel);

public record ServiceModel(
    ITypeDefinition ImplementationType,
    ConstructorInfoModel? Constructor,
    ServiceFactoryModel? Factory,
    FactoryOutputDelegate? FactoryOutput,
    IReadOnlyList<ServiceRegistrationModel> Registrations,
    RegistrationFeature Features,
    /// <summary>
    /// Environment conditions declared on the implementation, or null when it registers
    /// unconditionally. They sit on the service rather than on each registration because the
    /// attributes are declared on the class, so every registration it produces shares them and the
    /// writer emits one guard around the lot.
    /// </summary>
    IReadOnlyList<EnvironmentConditionModel>? Conditions = null,

    /// <summary>
    /// Where the implementation was declared, so a diagnostic about it can point at the class
    /// rather than at the project. Deliberately absent from
    /// <see cref="ServiceModelComparer"/> — it is not part of what makes two models the same
    /// registration, and including it would miss the incremental cache on an edit above the class.
    /// </summary>
    LocationModel? Location = null) {
    public static ServiceModel Ignore = new ServiceModel(
        TypeDefinition.Get("", "Ignore"),
        null,
        null,
        null,
        Array.Empty<ServiceRegistrationModel>(),
        RegistrationFeature.None
        );
}

public class ServiceModelComparer : IEqualityComparer<ServiceModel> {

    public bool Equals(ServiceModel? x, ServiceModel? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return
            x.Features == y.Features &&
            x.ImplementationType.Equals(y.ImplementationType) &&
            CompareConstructor(x.Constructor, y.Constructor) &&
            CompareRegistrations(x.Registrations, y.Registrations) &&
            CompareFactory(x.Factory, y.Factory) &&
            CompareFactoryOutput(x.FactoryOutput, y.FactoryOutput) &&
            CompareConditions(x.Conditions, y.Conditions);
    }

    private bool CompareConstructor(ConstructorInfoModel? xConstructor, ConstructorInfoModel? yConstructor) {
        if (xConstructor is null && yConstructor is null) return true;
        if (xConstructor is null || yConstructor is null) return false;
        return xConstructor.Parameters.SequenceEqual(yConstructor.Parameters);
    }

    private bool CompareFactoryOutput(FactoryOutputDelegate? xFactoryOutput, FactoryOutputDelegate? yFactoryOutput) {
        if (xFactoryOutput is null && yFactoryOutput is null) return true;
        if (xFactoryOutput is null || yFactoryOutput is null) return false;
        return true;
    }

    /// <summary>
    /// Null and empty are the same thing here — both mean "registers unconditionally" — so they
    /// have to compare equal or a model rebuilt from an edit elsewhere would miss the cache.
    /// </summary>
    private bool CompareConditions(
        IReadOnlyList<EnvironmentConditionModel>? xConditions,
        IReadOnlyList<EnvironmentConditionModel>? yConditions) {
        if ((xConditions?.Count ?? 0) == 0 && (yConditions?.Count ?? 0) == 0) {
            return true;
        }

        return ModelEquality.ListEquals(xConditions, yConditions);
    }

    private bool CompareFactory(ServiceFactoryModel? xFactory, ServiceFactoryModel? yFactory) {
        if (xFactory is null && yFactory is null) return true;
        if (xFactory is null) return false;
        if (yFactory is null) return false;
        return xFactory.Equals(yFactory);
    }

    public int GetHashCode(ServiceModel obj) {
        return obj.ImplementationType.GetHashCode();
    }

    private bool CompareRegistrations(IReadOnlyList<ServiceRegistrationModel> xRegistrations, IReadOnlyList<ServiceRegistrationModel> yRegistrations) {
        if (xRegistrations.Count != yRegistrations.Count) {
            return false;
        }

        for (var i = 0; i < xRegistrations.Count; i++) {
            var x = xRegistrations[i];
            var y = yRegistrations[i];

            if (!CompareRegistration(x, y)) {
                return false;
            }
        }

        return true;
    }

    private bool CompareRegistration(ServiceRegistrationModel x, ServiceRegistrationModel y) {
        return x.ServiceType.Equals(y.ServiceType) &&
               x.Lifestyle == y.Lifestyle &&
               x.RegistrationType == y.RegistrationType &&
               CompareNamespaces(x.Namespaces, y.Namespaces) &&
               Equals(x.Realm, y.Realm) &&
               Equals(x.Key, y.Key);
    }

    private bool CompareNamespaces(IReadOnlyList<string>? xNamespaces, IReadOnlyList<string>? yNamespaces) {
        if (xNamespaces is null && yNamespaces is null) return true;
        if (xNamespaces is null || yNamespaces is null) return false;
        if (xNamespaces.Count != yNamespaces.Count) return false;
        return xNamespaces.SequenceEqual(yNamespaces);
    }
}
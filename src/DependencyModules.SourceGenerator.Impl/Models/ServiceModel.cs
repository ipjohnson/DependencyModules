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

public record ServiceFactoryModel(
    ITypeDefinition TypeDefinition,
    string MethodName,
    IReadOnlyList<ParameterInfoModel> Parameters);

public record ServiceRegistrationModel(
    ITypeDefinition ServiceType,
    ServiceLifestyle Lifestyle,
    RegistrationType? RegistrationType = null,
    ITypeDefinition? Realm = null,
    object? Key = null,
    bool? CrossWire = false);

public record ServiceModel(
    ITypeDefinition ImplementationType,
    ServiceFactoryModel? Factory,
    IReadOnlyList<ServiceRegistrationModel> Registrations) {
    public static ServiceModel Ignore = new ServiceModel(
        TypeDefinition.Get("", "Ignore"),
        null,
        Array.Empty<ServiceRegistrationModel>()
        );
}

public class ServiceModelComparer : IEqualityComparer<ServiceModel> {

    public bool Equals(ServiceModel? x, ServiceModel? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return
            x.ImplementationType.Equals(y.ImplementationType) &&
            CompareRegistrations(x.Registrations, y.Registrations) && 
            CompareFactory(x.Factory, y.Factory);
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
               Equals(x.Realm, y.Realm) &&
               Equals(x.Key, y.Key);
    }
}
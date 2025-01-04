using CSharpAuthor;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Models;

public enum ServiceLifestyle {
    Transient,
    Scoped,
    Singleton
}

public record ServiceRegistrationModel(
    ITypeDefinition ServiceType,
    ServiceLifestyle Lifestyle,
    bool? RegisterWithTry,
    ITypeDefinition? Realm = null,
    object? Key = null);

public record ServiceModel(
    ITypeDefinition ImplementationType,
    IReadOnlyList<ServiceRegistrationModel> Registrations);

public class ServiceModelComparer : IEqualityComparer<ServiceModel> {

    public bool Equals(ServiceModel? x, ServiceModel? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return
            x.ImplementationType.Equals(y.ImplementationType) &&
            CompareRegistrations(x.Registrations, y.Registrations);
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
               x.RegisterWithTry == y.RegisterWithTry &&
               Equals(x.Realm, y.Realm) &&
               Equals(x.Key, y.Key);
    }

    public int GetHashCode(ServiceModel obj) {
        unchecked {
            return obj.ImplementationType.GetHashCode();
        }
    }
}
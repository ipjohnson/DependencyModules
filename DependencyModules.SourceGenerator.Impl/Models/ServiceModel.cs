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
    bool RegisterWithTry,
    bool RoutedRegistration,
    ITypeDefinition? Realm = null,
    object? Key = null);

public record ServiceModel(
    ITypeDefinition ImplementationType, IReadOnlyList<ServiceRegistrationModel> Registrations);

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

        return true;
    }

    public int GetHashCode(ServiceModel obj) {
        unchecked {
            return obj.ImplementationType.GetHashCode();
        }
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// Rewrites registrations so that a decorator wraps the implementation already registered for a
/// service type.
/// </summary>
/// <remarks>
/// Generated code calls into this rather than emitting the rewrite inline, because the same three
/// mistakes are easy to make each time it is written out: capturing the descriptor after replacing
/// it, decorating only the first match, and losing the original lifetime.
/// </remarks>
public static class DecoratorHelper {

    /// <summary>
    /// Wraps every registration of <paramref name="serviceType"/> using <paramref name="decoratorFactory"/>.
    /// </summary>
    /// <param name="services">The collection to rewrite.</param>
    /// <param name="serviceType">
    /// The service being decorated. For an open generic such as <c>IRepository&lt;&gt;</c>, every
    /// closed registration of it is wrapped.
    /// </param>
    /// <param name="decoratorFactory">
    /// Builds the decorator given the provider and the instance being wrapped.
    /// </param>
    public static void Decorate(
        IServiceCollection services,
        Type serviceType,
        Func<IServiceProvider, object, object> decoratorFactory) {

        for (var i = services.Count - 1; i >= 0; i--) {
            var descriptor = services[i];

            if (!Matches(descriptor.ServiceType, serviceType)) {
                continue;
            }

            // Captured before the slot is overwritten. Reading services[i] inside the factory would
            // close over the replacement and recurse until the stack runs out, at a point far from
            // the registration that caused it.
            var inner = descriptor;

            services[i] = descriptor.IsKeyedService
                ? new ServiceDescriptor(
                    descriptor.ServiceType,
                    descriptor.ServiceKey,
                    (provider, key) => decoratorFactory(provider, CreateKeyedInner(provider, key, inner)),
                    descriptor.Lifetime)
                : new ServiceDescriptor(
                    descriptor.ServiceType,
                    provider => decoratorFactory(provider, CreateInner(provider, inner)),
                    // The decorator must not change how long the service lives.
                    descriptor.Lifetime);
        }
    }

    /// <summary>
    /// True when a registered service type is the one being decorated, including a closed
    /// construction of a decorated open generic.
    /// </summary>
    private static bool Matches(Type registeredServiceType, Type decoratedServiceType) {
        if (registeredServiceType == decoratedServiceType) {
            return true;
        }

        return decoratedServiceType.IsGenericTypeDefinition &&
               registeredServiceType.IsGenericType &&
               registeredServiceType.GetGenericTypeDefinition() == decoratedServiceType;
    }

    /// <summary>
    /// Produces the instance being decorated, covering all three shapes a descriptor can take.
    /// </summary>
    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) {
        if (descriptor.ImplementationInstance != null) {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory != null) {
            return descriptor.ImplementationFactory(provider);
        }

        if (descriptor.ImplementationType != null) {
            return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"The registration for '{descriptor.ServiceType}' has no implementation type, factory, or " +
            "instance, so there is nothing to decorate.");
    }

    private static object CreateKeyedInner(IServiceProvider provider, object? key, ServiceDescriptor descriptor) {
        if (descriptor.KeyedImplementationInstance != null) {
            return descriptor.KeyedImplementationInstance;
        }

        if (descriptor.KeyedImplementationFactory != null) {
            return descriptor.KeyedImplementationFactory(provider, key);
        }

        if (descriptor.KeyedImplementationType != null) {
            return ActivatorUtilities.CreateInstance(provider, descriptor.KeyedImplementationType);
        }

        throw new InvalidOperationException(
            $"The keyed registration for '{descriptor.ServiceType}' has no implementation type, factory, " +
            "or instance, so there is nothing to decorate.");
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// Rewrites registrations so that a decorator wraps the implementation already registered for a
/// service type.
/// </summary>
/// <remarks>
/// <para>
/// Generated code calls into this rather than emitting the rewrite inline, because the same three
/// mistakes are easy to make each time it is written out: capturing the descriptor after replacing
/// it, decorating only the first match, and losing the original lifetime.
/// </para>
/// <para>
/// Nothing here reflects. There was a <c>Decorate(IServiceCollection, Type, Type)</c> overload that
/// closed a generic decorator with <c>Type.MakeGenericType</c>, recovered its type arguments from an
/// interface walk and built it with <c>ActivatorUtilities</c>. It worked under a JIT and failed in
/// every published Native AOT application — for a generic decorator because no instantiation was
/// statically reachable, and for a non-generic one because the trimmer had no reason to keep a
/// constructor nothing named. The generator emits one closed call per registration instead, so the
/// decorator is constructed by a literal <c>new</c> and the code exists in the assembly.
/// </para>
/// </remarks>
public static class DecoratorHelper {

    /// <summary>
    /// Swaps an <b>open generic</b> registration for a generated wrapper that implements the same
    /// open generic service.
    /// </summary>
    /// <param name="services">The collection to rewrite.</param>
    /// <param name="serviceType">The open generic service, such as <c>IRepository&lt;&gt;</c>.</param>
    /// <param name="implementationType">
    /// The open generic implementation currently registered for it, such as <c>Repository&lt;&gt;</c>.
    /// </param>
    /// <param name="wrapperType">
    /// The generated wrapper, such as <c>Repository_Intercepted&lt;&gt;</c>. It implements the service
    /// and takes the implementation as a constructor parameter.
    /// </param>
    /// <remarks>
    /// <para>
    /// <see cref="Decorate(IServiceCollection, Type, Func{IServiceProvider, object, object})"/> cannot
    /// serve this: it rewrites a registration into a factory, and the container rejects a factory for
    /// an open generic service type — "requires registering an open generic implementation type". An
    /// open generic implementation type is exactly what it does accept, and a generated wrapper is
    /// one.
    /// </para>
    /// <para>
    /// The implementation is additionally registered under its own concrete type, which is how the
    /// wrapper receives it without asking for the service it is itself registered as — that would
    /// resolve to the wrapper and recurse. Lifetime is carried across to both, so wrapping does not
    /// change how long anything lives.
    /// </para>
    /// <para>
    /// Idempotent: a second pass finds the wrapper in the slot rather than the implementation and
    /// leaves it alone, which is what keeps two modules carrying the same registration from
    /// double-wrapping.
    /// </para>
    /// </remarks>
    public static void InterceptOpenGeneric(
        IServiceCollection services,
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementationType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type wrapperType) {

        // Snapshotted: the loop appends the implementation's own registration, and re-reading Count
        // would walk into what it just added.
        var count = services.Count;

        for (var i = 0; i < count; i++) {
            var descriptor = services[i];

            if (descriptor.ServiceType != serviceType || ImplementationOf(descriptor) != implementationType) {
                continue;
            }

            services.Add(new ServiceDescriptor(implementationType, implementationType, descriptor.Lifetime));

            services[i] = descriptor.IsKeyedService
                ? new ServiceDescriptor(serviceType, descriptor.ServiceKey, wrapperType, descriptor.Lifetime)
                : new ServiceDescriptor(serviceType, wrapperType, descriptor.Lifetime);
        }
    }

    /// <summary>
    /// The implementation type of a descriptor, keyed or not.
    /// </summary>
    /// <remarks>
    /// A keyed descriptor throws from <c>ImplementationType</c> rather than returning null, so the two
    /// cannot be read through one property.
    /// </remarks>
    private static Type? ImplementationOf(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

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
    /// <remarks>
    /// Applying the same decorator twice layers it twice. That is the caller's choice here; the
    /// overload generated code uses takes an identity and refuses the second application.
    /// </remarks>
    public static void Decorate(
        IServiceCollection services,
        Type serviceType,
        Func<IServiceProvider, object, object> decoratorFactory) {

        Decorate(services, serviceType, decoratorFactory, null, null);
    }

    /// <summary>
    /// Wraps every registration of <typeparamref name="TService"/> in the decorator
    /// <paramref name="decoratorFactory"/> constructs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the overload generated code calls, and the service is a type parameter rather than a
    /// <see cref="Type"/> deliberately. A type argument cannot be an unbound generic, so a request to
    /// decorate an open generic service — which the container cannot honour, because decoration
    /// replaces a registration with a factory — becomes impossible to express rather than something
    /// to detect and throw about at composition.
    /// </para>
    /// <para>
    /// It also means the generator emits one closed call per closed registration, constructing the
    /// decorator with a literal <c>new</c>. Nothing here closes a generic type, selects a constructor
    /// or walks an interface list at run time, so a published Native AOT build has the code it needs
    /// for every decorator the build emitted — which the reflective route could not guarantee, and
    /// silently failed to provide for a value-type type argument.
    /// </para>
    /// </remarks>
    public static void Decorate<TService>(
        IServiceCollection services,
        Type decoratorIdentity,
        Func<IServiceProvider, TService, TService> decoratorFactory) where TService : class {

        Decorate(services, decoratorIdentity, decoratorFactory, null);
    }

    /// <summary>
    /// Wraps only the registration of <typeparamref name="TService"/> that was built from one
    /// particular implementation.
    /// </summary>
    /// <param name="services">The collection to rewrite.</param>
    /// <param name="decoratorIdentity">
    /// The wrapper type, used to refuse a second application of the same wrapper to one registration.
    /// </param>
    /// <param name="decoratorFactory">
    /// Builds the wrapper given the provider and the instance being wrapped.
    /// </param>
    /// <param name="implementationType">
    /// Restricts the rewrite to the registration built from this implementation. Null wraps every
    /// registration of the service, which is what a decorator declared against an interface should
    /// do — so decoration keeps calling the three-argument overload.
    /// </param>
    /// <remarks>
    /// <para>
    /// An interceptor passes one. Its wrapper is generated from a single class and forwards that
    /// class's members, so applying it to a sibling implementation of the same interface wrapped a
    /// service that never asked to be intercepted, and reported the sibling's type as the first
    /// class's wrapper. With both implementations marked, each registration was wrapped once per
    /// generated wrapper and every interceptor ran twice per call — no exception, just doubled
    /// metrics, audit rows and retries.
    /// </para>
    /// <para>
    /// A separate overload rather than an optional parameter on the one above: generated code from
    /// an earlier version is compiled against the three-argument signature, and widening that one
    /// would leave it unable to bind against a newer runtime.
    /// </para>
    /// </remarks>
    public static void Decorate<TService>(
        IServiceCollection services,
        Type decoratorIdentity,
        Func<IServiceProvider, TService, TService> decoratorFactory,
        Type? implementationType) where TService : class {

        Decorate(
            services,
            typeof(TService),
            (provider, inner) => decoratorFactory(provider, (TService)inner),
            null,
            decoratorIdentity,
            implementationType);
    }

    /// <summary>
    /// Which decorators have already been applied to a descriptor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One decorator can be emitted from more than one place. A generic decorator over
    /// <c>IHandler&lt;,&gt;</c> is expanded once against the registrations the attributes made and
    /// once against the registrations the conventions made, and both emissions name the same closed
    /// service — so without this the implementation behind it is wrapped twice.
    /// </para>
    /// <para>
    /// The symptom is not an exception. It is a decorator's side effects happening twice per call:
    /// two log lines, two audit entries, a cache consulted twice. Nothing fails and nothing is
    /// reported, which is why this is a guard rather than a diagnostic.
    /// </para>
    /// <para>
    /// Keyed weakly on the descriptor, so entries die with the collection. Decoration rewrites a
    /// descriptor rather than mutating it, so what a replacement inherits has to be carried across
    /// explicitly — see <see cref="RecordApplied"/>.
    /// </para>
    /// </remarks>
    private static readonly ConditionalWeakTable<ServiceDescriptor, HashSet<Type>> Applied = new();

    private static bool AlreadyApplied(ServiceDescriptor descriptor, Type? decoratorIdentity) =>
        decoratorIdentity != null &&
        Applied.TryGetValue(descriptor, out var applied) &&
        applied.Contains(decoratorIdentity);

    private static void RecordApplied(
        ServiceDescriptor original, ServiceDescriptor replacement, Type? decoratorIdentity) {

        if (decoratorIdentity == null) {
            return;
        }

        var applied = new HashSet<Type>();

        // Stacked decorators each rewrite the slot, so the set has to follow the descriptor that
        // now occupies it rather than staying with the one that was replaced.
        if (Applied.TryGetValue(original, out var existing)) {
            applied.UnionWith(existing);
        }

        applied.Add(decoratorIdentity);

        Applied.Remove(replacement);
        Applied.Add(replacement, applied);
    }

    /// <summary>
    /// The implementation a descriptor was originally built from, across rewrites.
    /// </summary>
    /// <remarks>
    /// Decoration replaces an implementation-type descriptor with a factory, which erases the
    /// implementation from the descriptor itself. An interceptor ordered after a decorator would
    /// then be unable to recognise its own registration, so the origin is carried across the
    /// replacement the same way <see cref="Applied"/> is.
    /// </remarks>
    private static readonly ConditionalWeakTable<ServiceDescriptor, Type> OriginImplementation = new();

    /// <summary>
    /// The implementation behind a descriptor, or null when it cannot be known — a registration made
    /// from an instance or a hand-written factory, including the factories
    /// <c>DependencyModules_GenerateFactories</c> emits.
    /// </summary>
    private static Type? OriginImplementationOf(ServiceDescriptor descriptor) =>
        OriginImplementation.TryGetValue(descriptor, out var origin) ? origin : ImplementationOf(descriptor);

    private static void RecordOrigin(ServiceDescriptor original, ServiceDescriptor replacement) {
        if (OriginImplementationOf(original) is not { } origin) {
            return;
        }

        OriginImplementation.Remove(replacement);
        OriginImplementation.Add(replacement, origin);
    }

    private static void Decorate(
        IServiceCollection services,
        Type serviceType,
        Func<IServiceProvider, object, object> decoratorFactory,
        Type? decoratorType,
        Type? decoratorIdentity,
        Type? implementationType = null) {

        var ordinal = 0;

        for (var i = services.Count - 1; i >= 0; i--) {
            var descriptor = services[i];

            if (!Matches(descriptor.ServiceType, serviceType)) {
                continue;
            }

            // A registration this method displaced on an earlier pass is machinery, not a service.
            // Decorating it would wrap the implementation a second time, one layer further in.
            if (descriptor.ServiceKey is DisplacedImplementationKey) {
                continue;
            }

            // Emitted from two places for the same registration; the first one wins.
            if (AlreadyApplied(descriptor, decoratorIdentity)) {
                continue;
            }

            // An interceptor's wrapper belongs to the one implementation it was generated from.
            // Skipped only when the descriptor's origin is known and is a different type: a
            // registration made from an instance or a factory cannot be attributed to an
            // implementation, and refusing to wrap it there would silently stop intercepting a
            // service that had asked for it — a worse failure than the one this filter prevents.
            if (implementationType != null &&
                OriginImplementationOf(descriptor) is { } origin &&
                origin != implementationType) {
                continue;
            }

            GuardOpenGenericRegistration(descriptor, decoratorType);

            // Captured before the slot is overwritten. Reading services[i] inside the factory would
            // close over the replacement and recurse until the stack runs out, at a point far from
            // the registration that caused it.
            var innerFactory = CaptureInner(services, descriptor, ordinal++);

            var replacement = descriptor.IsKeyedService
                ? new ServiceDescriptor(
                    descriptor.ServiceType,
                    descriptor.ServiceKey,
                    (provider, key) => decoratorFactory(provider, innerFactory(provider, key)),
                    descriptor.Lifetime)
                : new ServiceDescriptor(
                    descriptor.ServiceType,
                    provider => decoratorFactory(provider, innerFactory(provider, null)),
                    // The decorator must not change how long the service lives.
                    descriptor.Lifetime);

            RecordApplied(descriptor, replacement, decoratorIdentity);
            RecordOrigin(descriptor, replacement);

            services[i] = replacement;
        }
    }

    /// <summary>
    /// Identifies a registration displaced by decoration, so the container keeps building it.
    /// </summary>
    /// <remarks>
    /// Deterministic rather than a counter shared across calls: the same collection decorated the
    /// same way has to produce the same keys, or two builds of one project differ for no reason. The
    /// ordinal only separates descriptors that are otherwise identical, which is legal —
    /// <c>AddSingleton&lt;IFoo, Foo&gt;()</c> twice registers two services and must stay two.
    /// </remarks>
    private sealed class DisplacedImplementationKey : IEquatable<DisplacedImplementationKey> {
        private readonly Type _serviceType;
        private readonly Type _implementationType;
        private readonly int _ordinal;

        public DisplacedImplementationKey(Type serviceType, Type implementationType, int ordinal) {
            _serviceType = serviceType;
            _implementationType = implementationType;
            _ordinal = ordinal;
        }

        public bool Equals(DisplacedImplementationKey? other) =>
            other is not null &&
            _serviceType == other._serviceType &&
            _implementationType == other._implementationType &&
            _ordinal == other._ordinal;

        public override bool Equals(object? obj) => Equals(obj as DisplacedImplementationKey);

        public override int GetHashCode() {
            unchecked {
                var hash = _serviceType.GetHashCode();
                hash = hash * 31 + _implementationType.GetHashCode();
                return hash * 31 + _ordinal;
            }
        }

        public override string ToString() =>
            $"DependencyModules decorated '{_serviceType}' -> '{_implementationType}' #{_ordinal}";
    }

    /// <summary>
    /// Produces the instance being decorated, covering all three shapes a descriptor can take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An instance is returned and a factory is invoked; neither needs anything from this library. An
    /// implementation type is the awkward one, because decoration has replaced the registration that
    /// told the container to build it.
    /// </para>
    /// <para>
    /// It is <b>not</b> built here. Constructing it with <c>ActivatorUtilities</c> hands back an
    /// object the container never created, and therefore never tracks — so a scoped
    /// <see cref="IDisposable"/> stopped being disposed the moment it was decorated, silently, on
    /// every runtime. The registration is displaced under a private key instead, so construction,
    /// constructor selection and disposal ownership all stay exactly where they were.
    /// </para>
    /// </remarks>
    private static Func<IServiceProvider, object?, object> CaptureInner(
        IServiceCollection services, ServiceDescriptor descriptor, int ordinal) {

        if (descriptor.IsKeyedService) {
            if (descriptor.KeyedImplementationInstance is { } keyedInstance) {
                return (_, _) => keyedInstance;
            }

            if (descriptor.KeyedImplementationFactory is { } keyedFactory) {
                return (provider, key) => keyedFactory(provider, key);
            }

            if (descriptor.KeyedImplementationType is { } keyedImplementationType) {
                var keyedInnerKey = Displace(
                    services, descriptor.ServiceType, keyedImplementationType, ordinal, descriptor.Lifetime);

                return (provider, _) => provider.GetRequiredKeyedService(keyedImplementationType, keyedInnerKey);
            }

            throw new InvalidOperationException(
                $"The keyed registration for '{descriptor.ServiceType}' has no implementation type, factory, " +
                "or instance, so there is nothing to decorate.");
        }

        if (descriptor.ImplementationInstance is { } instance) {
            return (_, _) => instance;
        }

        if (descriptor.ImplementationFactory is { } factory) {
            return (provider, _) => factory(provider);
        }

        if (descriptor.ImplementationType is { } implementationType) {
            var innerKey = Displace(
                services, descriptor.ServiceType, implementationType, ordinal, descriptor.Lifetime);

            return (provider, _) => provider.GetRequiredKeyedService(implementationType, innerKey);
        }

        throw new InvalidOperationException(
            $"The registration for '{descriptor.ServiceType}' has no implementation type, factory, or " +
            "instance, so there is nothing to decorate.");
    }

    /// <summary>
    /// Re-registers an implementation the decoration displaced, under a key nothing else can name.
    /// </summary>
    private static DisplacedImplementationKey Displace(
        IServiceCollection services,
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementationType,
        int ordinal,
        ServiceLifetime lifetime) {

        var key = new DisplacedImplementationKey(serviceType, implementationType, ordinal);

        // Appended while the caller iterates backwards, so it is never revisited on this pass.
        services.Add(new ServiceDescriptor(implementationType, key, implementationType, lifetime));

        return key;
    }

    /// <summary>
    /// Refuses to decorate a registration made against an open generic service type.
    /// </summary>
    /// <remarks>
    /// Decoration replaces a registration with a factory, and the container will not accept a
    /// factory for an open generic service type — it needs an open generic implementation type so it
    /// can close one per request. Registering the rewrite anyway throws from
    /// <c>BuildServiceProvider</c>, naming the service and nothing else, arbitrarily far from the
    /// declaration that caused it.
    ///
    /// This cannot be caught when the code is generated: a decorator names an open generic service
    /// type in order to match every closed registration of it, which works, and whether any given
    /// registration is open or closed is only known here.
    ///
    /// Closed registrations of a generic service are decorated normally, so declaring a closed
    /// construction is the way through.
    /// </remarks>
    private static void GuardOpenGenericRegistration(ServiceDescriptor descriptor, Type? decoratorType) {
        if (!descriptor.ServiceType.IsGenericTypeDefinition) {
            return;
        }

        var by = decoratorType == null ? "" : $" by '{decoratorType}'";

        throw new InvalidOperationException(
            $"'{descriptor.ServiceType}' is registered as an open generic and cannot be decorated{by}. " +
            "Decorating replaces a registration with a factory, which the container does not allow " +
            "for an open generic service type. Register closed constructions of the service instead, " +
            "such as a class deriving from the generic implementation.");
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

}

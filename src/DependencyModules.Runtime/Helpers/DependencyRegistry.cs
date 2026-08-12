using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// Delegate representing a function responsible for registering dependencies
/// into an IServiceCollection.
/// </summary>
/// <param name="serviceCollection">The IServiceCollection to which dependencies will be added.</param>
public delegate void RegistryFunc(IServiceCollection serviceCollection);

/// <summary>
/// Delegate representing a function responsible for registering dependencies into an
/// IServiceCollection, with the environment those registrations may be conditional on.
/// </summary>
/// <remarks>
/// Generated code uses this form only when a module declares an environment condition. Everything
/// registered through <see cref="RegistryFunc"/> is adapted to it, so both kinds keep their
/// declaration order relative to each other — which matters, because the container resolves a
/// single service from the last matching descriptor.
/// </remarks>
/// <param name="serviceCollection">The IServiceCollection to which dependencies will be added.</param>
/// <param name="environment">The environment conditions are evaluated against. Never null.</param>
public delegate void EnvironmentRegistryFunc(
    IServiceCollection serviceCollection, IModuleEnvironment environment);

/// <summary>
///     Static class used to store dependency registration functions
///     per type
/// </summary>
/// <typeparam name="T"></typeparam>
// ReSharper disable once ClassNeverInstantiated.Global
public class DependencyRegistry<T> {
    // ReSharper disable StaticMemberInGenericType
    private static readonly object SyncLock = new();
    private static List<EnvironmentRegistryFunc>? RegistryFuncs;
    private static List<DecoratorRegistration>? Decorators;
    private static List<IDependencyModule>? Modules;

    /// <summary>
    ///     Add registration func
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(RegistryFunc registryFunc) {
        lock (SyncLock) {
            (RegistryFuncs ??= []).Add((serviceCollection, _) => registryFunc(serviceCollection));
        }

        return 1;
    }

    /// <summary>
    ///     Add registration func whose registrations may depend on the environment
    /// </summary>
    /// <param name="registryFunc"></param>
    /// <returns></returns>
    public static int Add(EnvironmentRegistryFunc registryFunc) {
        lock (SyncLock) {
            (RegistryFuncs ??= []).Add(registryFunc);
        }

        return 1;
    }

    /// <summary>
    /// Adding singleton instance, intended to be used as a short cut
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="lifetime"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static int Add<TInstance>(
        Func<IServiceProvider, TInstance> provider,
        ServiceLifetime lifetime = ServiceLifetime.Transient) where TInstance : class {
        lock (SyncLock) {
            (RegistryFuncs ??= []).Add(
                (registry, _) => registry.Add(
                    new ServiceDescriptor(
                        typeof(TInstance),
                        provider,
                        lifetime
                    )));
        }
        return 1;
    }

    /// <summary>
    /// Add instance of of dependency
    /// </summary>
    /// <param name="implementationType">
    /// The implementation to register. Annotated so the constructor requirement
    /// <see cref="ServiceDescriptor"/> declares reaches the type the caller named — without it the
    /// requirement stops here, and a trimmed build can remove the constructor of a type whose
    /// <c>typeof()</c> is right there in the generated code.
    /// </param>
    /// <param name="lifetime"></param>
    /// <param name="serviceKey"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static int Add<TInstance>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementationType,
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        object? serviceKey = null) where TInstance : class {
        lock (SyncLock) {
            (RegistryFuncs ??= []).Add(
                (registry, _) => registry.Add(
                    new ServiceDescriptor(
                        typeof(TInstance),
                        serviceKey,
                        implementationType,
                        lifetime
                    )));
        }
        return 1;
    }

    /// <summary>
    ///      Add decorator func
    /// </summary>
    /// <param name="registryFunc">Function that decorates registrations already in the collection.</param>
    /// <param name="order">
    ///     Controls how decorators nest. Lower values are applied first and therefore sit closer to
    ///     the implementation; higher values wrap them. Decorators sharing an order are applied in
    ///     registration order.
    ///
    ///     By convention, framework packages use 0-999 and application code uses 1000 and above, so
    ///     that an application's decorators wrap those contributed by the libraries it consumes.
    /// </param>
    /// <returns></returns>
    public static int AddDecorator(RegistryFunc registryFunc, int order = 0) {
        lock (SyncLock) {
            (Decorators ??= []).Add(new DecoratorRegistration(order, registryFunc));
        }

        return 1;
    }

    /// <summary>
    ///      Add decorator func whose application may depend on the environment
    /// </summary>
    /// <remarks>
    /// Generated code uses this form only when a decorator declares an environment condition.
    /// A decorator that does not apply is simply never invoked, so the service resolves undecorated
    /// rather than being wrapped by something that checks the environment on every call.
    /// </remarks>
    /// <param name="registryFunc">Function that decorates registrations already in the collection.</param>
    /// <param name="order">See the other overload; ordering is unaffected by the condition.</param>
    /// <returns></returns>
    public static int AddDecorator(EnvironmentRegistryFunc registryFunc, int order = 0) {
        lock (SyncLock) {
            (Decorators ??= []).Add(new DecoratorRegistration(order, registryFunc));
        }

        return 1;
    }

    /// <summary>
    /// Add module
    /// </summary>
    /// <param name="modules"></param>
    /// <returns></returns>
    public static int AddModule(params IDependencyModule[] modules) {
        lock (SyncLock) {
            (Modules ??= []).AddRange(modules);
        }

        return 1;
    }

    /// <summary>
    ///     Load modules into service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="dependencyModules"></param>
    public static void LoadModules(IServiceCollection serviceCollection, params IDependencyModule[] dependencyModules) {
        var modules = GetAllModules(dependencyModules);
        
        ApplyFeatures(serviceCollection, modules);

        ApplyServices(serviceCollection, modules);
        
        ApplyDecorators(serviceCollection, modules);
    }
    
    /// <summary>
    ///     Apply all registration for a given type to the service collection
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyServices(IServiceCollection serviceCollection) {
        ApplyServices(serviceCollection, FindOrCreateEnvironment(serviceCollection));
    }

    /// <summary>
    ///     Apply all registration for a given type to the service collection, evaluating any
    ///     environment conditions against the supplied environment
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="environment"></param>
    public static void ApplyServices(IServiceCollection serviceCollection, IModuleEnvironment environment) {
        EnvironmentRegistryFunc[] snapshot;
        lock (SyncLock) {
            if (RegistryFuncs == null) {
                return;
            }
            snapshot = RegistryFuncs.ToArray();
        }

        foreach (var registryFunc in snapshot) {
            registryFunc(serviceCollection, environment);
        }
    }

    /// <summary>
    /// Apply all decorators
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection) {
        ApplyDecorators(serviceCollection, FindOrCreateEnvironment(serviceCollection));
    }

    /// <summary>
    /// The environment already in the collection, or a fresh default.
    /// </summary>
    /// <remarks>
    /// Deliberately does not register what it creates. These overloads apply registrations to a
    /// collection the caller owns, and adding a descriptor nobody asked for would change what they
    /// hand back. An environment the caller <i>did</i> supply is found and shared, which is the case
    /// where two calls disagreeing would actually matter — two process defaults read the same
    /// variables and give the same answers.
    /// </remarks>
    private static IModuleEnvironment FindOrCreateEnvironment(IServiceCollection serviceCollection) {
        var environment = FindModuleEnvironment(serviceCollection);

        return environment ?? ModuleEnvironment.CreateDefault();
    }

    /// <summary>
    /// Stable insertion sort by Order. Replaces OrderBy so that no LINQ ordering machinery is
    /// instantiated for DecoratorRegistration at startup.
    /// </summary>
    private static void SortByOrder(List<DecoratorRegistration> list) {
        for (var i = 1; i < list.Count; i++) {
            var item = list[i];
            var j = i - 1;

            while (j >= 0 && list[j].Order > item.Order) {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = item;
        }
    }

    /// <summary>
    /// The environment already in the collection, or a new default registered into it.
    /// </summary>
    /// <remarks>
    /// Registered, not just used. Otherwise conditions would be decided by an environment that
    /// <c>GetRequiredService&lt;IModuleEnvironment&gt;()</c> then throws for. Only when nothing
    /// supplied one, so an application's own environment is never displaced — and registering it is
    /// what lets decoration find the same instance the registrations were decided against, which
    /// matters now that <c>CreateDefault</c> builds a fresh one per call.
    /// </remarks>
    private static IModuleEnvironment ResolveEnvironment(IServiceCollection serviceCollection) {
        var environment = FindModuleEnvironment(serviceCollection);

        if (environment != null) {
            return environment;
        }

        environment = ModuleEnvironment.CreateDefault();

        // The descriptor is built directly rather than through AddSingleton<T>. The extension
        // method is generic, and instantiating it here was one more thing to compile on a path
        // every application walks exactly once.
        serviceCollection.Add(new ServiceDescriptor(typeof(IModuleEnvironment), environment));

        return environment;
    }

    /// <summary>
    /// Apply all decorators, evaluating any environment conditions against the supplied environment
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="environment"></param>
    public static void ApplyDecorators(IServiceCollection serviceCollection, IModuleEnvironment environment) {
        var list = new List<DecoratorRegistration>(GetDecorators());
        SortByOrder(list);
        for (var i = 0; i < list.Count; i++) {
            list[i].RegistryFunc(serviceCollection, environment);
        }
    }

    /// <summary>
    /// The decorators registered for this type, unsorted.
    /// </summary>
    /// <remarks>
    /// Generated modules return these from <see cref="IDependencyModule.InternalGetDecorators"/> so
    /// that decorators from every module can be sorted together. Applying each module's decorators
    /// separately would make module discovery order outrank the declared order.
    /// </remarks>
    public static IReadOnlyList<DecoratorRegistration> GetDecorators() {
        lock (SyncLock) {
            return Decorators == null ? Array.Empty<DecoratorRegistration>() : Decorators.ToArray();
        }
    }

    /// <summary>
    /// GetModules that have been registered
    /// </summary>
    /// <param name="modules"></param>
    /// <returns></returns>
    public static IEnumerable<object> GetModules(params object[] modules) {
        lock (SyncLock) {
            if (Modules == null || Modules.Count == 0) {
                return modules;
            }

            return CombineWithAddedModules(Modules, modules);
        }
    }

    /// <summary>
    /// The AddModule case, split out so that the common path does not carry it.
    /// </summary>
    /// <remarks>
    /// Kept in its own non-inlined method because JIT-ing a method resolves every token in it, and
    /// the tokens here pull in System.Linq. Inline, that assembly was loaded during startup for
    /// every application, including the overwhelming majority that never call <c>AddModule</c> and
    /// return on the line above.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<object> CombineWithAddedModules(
        List<IDependencyModule> registered, object[] modules) {

        var snapshot = registered.ToList();

        return modules.Length == 0 ? snapshot : snapshot.Concat(modules);
    }

    private static void ApplyDecorators(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        // Gathered from every module and sorted together, the same way ApplyFeatures collects and
        // sorts feature applicators. Applying each module's decorators in turn would let module
        // discovery order outrank the declared order, which breaks a pipeline assembled from more
        // than one package.
        List<DecoratorRegistration>? decorators = null;

        for (var i = 0; i < modules.Count; i++) {
            var registrations = modules[i].InternalGetDecorators();

            // Tested before enumerating. A module with no decorators is the common case, and asking
            // it for an enumerator to immediately find it empty built one per module per startup.
            if (registrations is ICollection<DecoratorRegistration> { Count: 0 }) {
                continue;
            }

            foreach (var registration in registrations) {
                (decorators ??= []).Add(registration);
            }
        }

        if (decorators != null) {
            // The same environment the registrations were decided against. ApplyServices runs first
            // and registers one when nothing supplied it, so this finds that instance rather than
            // building a second answer to "what environment is this" — a decorator gated on
            // Development must not apply next to a service that decided it was in Production.
            // CreateDefault returns a fresh instance per call, so falling back to it here rather
            // than to the registered one would be exactly that divergence.
            var environment = ResolveEnvironment(serviceCollection);

            SortByOrder(decorators);

            for (var i = 0; i < decorators.Count; i++) {
                decorators[i].RegistryFunc(serviceCollection, environment);
            }
        }

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];

            // Retained for hand-written modules that decorate directly. Generated modules use
            // InternalGetDecorators so their decorators take part in the global ordering.
            module.InternalApplyDecorators(serviceCollection);

            // Mirrors how ApplyServices invokes ConfigureServices. Runs last, so the manual escape
            // hatch sees every declared decorator already in place.
            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureDecorators(serviceCollection);
            }
        }
    }

    private static void ApplyServices(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        // Always looked for now. Attribute conditions live on generated modules, which do not
        // implement IEnvironmentServiceCollectionConfiguration, so the old "only if some module
        // asked for it" gate would have missed them. It costs one scan of the collection per
        // AddModules call.
        // One environment for the whole call, and never null. Handing attribute conditions the
        // process default while handing IEnvironmentServiceCollectionConfiguration a null would let
        // one module see two different answers to "what environment is this" — the conditions
        // evaluating against Production while its own ConfigureServices was told there is no
        // environment at all. An application with no environment says so with ModuleEnvironment.None.
        // Nothing to apply means nothing to decide, so the collection is left exactly as it was
        // rather than picking up an environment nobody asked for.
        if (modules.Count == 0) {
            return;
        }

        // Registered, not just used. Otherwise conditions would be decided by an environment that
        // GetRequiredService<IModuleEnvironment>() then throws for, which is the same inconsistency
        // one layer out. Only when nothing supplied one, so an application's own environment is
        // never displaced — and registering it is what lets ApplyDecorators find the same instance.
        var environment = ResolveEnvironment(serviceCollection);

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            module.InternalApplyServices(serviceCollection, environment);

            if (module is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }

            if (module is IEnvironmentServiceCollectionConfiguration environmentConfigure) {
                environmentConfigure.ConfigureServices(serviceCollection, environment);
            }
        }
    }

    /// <summary>
    /// Refuses an <see cref="IModuleEnvironment"/> registered in a form that cannot decide
    /// registrations.
    /// </summary>
    /// <remarks>
    /// The environment is read while the collection is still being populated, so there is no
    /// provider to construct it from and only an instance can be used. Registered by type or by
    /// factory it was previously ignored without a word, the process default was used instead, and
    /// the registration that was ignored got shadowed by the one added in its place — a service
    /// gated on "Development" quietly took its production branch.
    /// </remarks>
    private static void RefuseUnusableEnvironment(ServiceDescriptor descriptor) {
        if (descriptor.ServiceType == typeof(IModuleEnvironment)) {
            throw new InvalidOperationException(
                "An IModuleEnvironment is registered, but not as a singleton instance, so it cannot " +
                "be used. The environment decides which services are registered, which happens " +
                "while the service collection is being populated and before any provider exists to " +
                "construct it from. Register the instance directly with " +
                "AddSingleton<IModuleEnvironment>(new MyEnvironment()), or pass it to " +
                "AddModules(environment, modules).");
        }
    }

    /// <summary>
    /// The usable environment in the collection, or null, refusing an unusable one on the way past.
    /// </summary>
    /// <remarks>
    /// One scan rather than two. The two questions - "is there an environment" and "is it in a form
    /// that can be used" - are answered by the same descriptor, and asking them separately walked
    /// the collection twice on every AddModules call.
    ///
    /// The last matching descriptor decides, because that is the one the container would resolve.
    /// Anything earlier is shadowed and cannot be what the application meant.
    /// </remarks>
    private static IModuleEnvironment? FindModuleEnvironment(IServiceCollection serviceCollection) {
        for (var i = serviceCollection.Count - 1; i >= 0; i--) {
            var descriptor = serviceCollection[i];

            if (descriptor.ServiceType != typeof(IModuleEnvironment)) {
                continue;
            }

            if (descriptor is {
                    Lifetime: ServiceLifetime.Singleton,
                    ImplementationInstance: IModuleEnvironment environment
                }) {
                return environment;
            }

            RefuseUnusableEnvironment(descriptor);
        }

        return null;
    }

    private static void ApplyFeatures(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        List<IFeatureApplicator>? features = null;

        for (var i = 0; i < modules.Count; i++) {
            var module = modules[i];
            
            if (module is IDependencyModuleApplicatorProvider provider) {
                foreach (var featureApplicator in provider.FeatureApplicators()) {
                    (features ??= []).Add(featureApplicator);
                }
            }
        }
        
        if (features != null) {
            features.Sort((x, y) => x.Order.CompareTo(y.Order));

            for (var i = 0; i < features.Count; i++) {
                var feature = features[i];
                feature.Apply(serviceCollection, modules);
            }
        }
    }


    private static IReadOnlyList<IDependencyModule> GetAllModules(IDependencyModule[] dependencyModules) {
        var list = new List<IDependencyModule>();

        foreach (var dependencyModule in dependencyModules) {
            InternalGetModules(dependencyModule, list);
        }

        return list;
    }

    
    private static void InternalGetModules(IDependencyModule dependencyModule, List<IDependencyModule> allDependencyModules) {
        if (!dependencyModule.LoadModule ||
            AlreadySeen(allDependencyModules, dependencyModule)) {
            return;
        }

        allDependencyModules.Insert(0, dependencyModule);

        var declared = dependencyModule.InternalGetModules();

        if (declared is not ICollection<object> { Count: 0 }) {
            foreach (var dependencyObject in declared) {
                if (dependencyObject is IDependencyModuleProvider moduleProvider) {
                    var dep = moduleProvider.GetModule();
                    InternalGetModules(dep, allDependencyModules);
                }
                else if (dependencyObject is IDependencyModule module) {
                    InternalGetModules(module, allDependencyModules);
                }
            }
        }

        var overridden = dependencyModule.GetModules();

        // Both lists are empty for the overwhelming majority of modules, and both are reached
        // through an interface. Testing for an empty collection first avoids building an enumerator
        // for each of them on every module of every startup.
        if (overridden is ICollection<IDependencyModule> { Count: 0 }) {
            return;
        }

        foreach (var module in overridden) {
            InternalGetModules(module, allDependencyModules);
        }
    }

    /// <summary>
    /// Whether this module is already in the list, by the module's own equality.
    /// </summary>
    /// <remarks>
    /// Written out rather than <c>List.Contains</c>. That routes through
    /// <c>EqualityComparer&lt;IDependencyModule&gt;.Default</c>, and building the default comparer
    /// for an interface is a runtime type-construction step - it showed up as the single most
    /// expensive thing in module discovery, to compare a list that usually holds one item.
    /// </remarks>
    private static bool AlreadySeen(List<IDependencyModule> modules, IDependencyModule candidate) {
        for (var i = 0; i < modules.Count; i++) {
            // Argument order matches EqualityComparer<T>.Default, which asks the element rather
            // than the candidate, so a hand-written asymmetric Equals behaves as it always did.
            if (modules[i].Equals(candidate)) {
                return true;
            }
        }

        return false;
    }
}
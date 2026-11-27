using DependencyModules.Runtime;
using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

/// <summary>
/// Covers how DependencyRegistry.LoadModules walks a module graph: de-duplication, ordering,
/// feature application, and environment-aware configuration.
/// </summary>
public partial class ModuleLoadingTests
{
    private interface IThing;

    private class Thing : IThing;

    [VerifyTest]
    public void LoadModules_AppliesEachModuleOnce()
    {
        var module = new CountingModule();

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, module, module);

        Verify(module.ApplyCount == 1);
    }

    [VerifyTest]
    public void LoadModules_DeduplicatesEqualModules()
    {
        var first = new EquatableModule("same");
        var second = new EquatableModule("same");

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, first, second);

        Verify(first.ApplyCount + second.ApplyCount == 1);
    }

    [VerifyTest]
    public void LoadModules_KeepsModulesThatCompareUnequal()
    {
        var first = new EquatableModule("one");
        var second = new EquatableModule("two");

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, first, second);

        Verify(first.ApplyCount == 1);
        Verify(second.ApplyCount == 1);
    }

    [VerifyTest]
    public void LoadModules_SkipsModulesThatOptOut()
    {
        var module = new CountingModule { LoadModule = false };

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, module);

        Verify(module.ApplyCount == 0);
    }

    [VerifyTest]
    public void LoadModules_LoadsNestedModulesReturnedByGetModules()
    {
        var child = new CountingModule();
        var parent = new CountingModule { Children = [child] };

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, parent);

        Verify(parent.ApplyCount == 1);
        Verify(child.ApplyCount == 1);
    }

    [VerifyTest]
    public void LoadModules_TerminatesOnCircularModuleReferences()
    {
        var first = new CountingModule();
        var second = new CountingModule { Children = [first] };
        first.Children = [second];

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, first);

        Verify(first.ApplyCount == 1);
        Verify(second.ApplyCount == 1);
    }

    [VerifyTest]
    public void LoadModules_InvokesServiceCollectionConfiguration()
    {
        var module = new ConfiguringModule();

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, module);

        Verify(collection.Any(descriptor => descriptor.ServiceType == typeof(IThing)));
    }

    [VerifyTest]
    public void LoadModules_AppliesFeaturesBeforeServices()
    {
        var module = new FeatureModule();

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, module);

        Verify(module.FeatureApplied);
        Verify(module.FeatureAppliedBeforeConfigure);
    }

    /// <summary>
    /// Regression test: ConfigureDecorators was declared on IServiceCollectionConfiguration and
    /// never invoked by anything, so a module implementing it silently did nothing.
    /// </summary>
    [VerifyTest]
    public void LoadModules_InvokesConfigureDecorators()
    {
        var module = new DecoratingModule();

        DependencyRegistry<object>.LoadModules(new ServiceCollection(), module);

        Verify(module.ConfigureDecoratorsCalled);
    }

    /// <summary>
    /// Decoration rewrites existing registrations, so it has to run after every module has
    /// registered its services or there would be nothing to decorate.
    /// </summary>
    [VerifyTest]
    public void LoadModules_RunsConfigureDecoratorsAfterAllServices()
    {
        var decorating = new DecoratingModule();
        var registering = new ConfiguringModule();

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, decorating, registering);

        Verify(decorating.ConfigureDecoratorsCalled);
        Verify(decorating.ServicesVisibleWhenDecorating!.Contains(typeof(IThing)));
    }

    [VerifyTest]
    public void LoadModules_CanDecorateARegistrationFromAnotherModule()
    {
        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(
            collection,
            new DecoratingModule(),
            new ConfiguringModule()
        );

        var provider = collection.BuildServiceProvider();

        var thing = provider.GetService<IThing>();
        Verify(thing?.GetType() == typeof(DecoratedThing));
    }

    [VerifyTest]
    public void LoadModules_AppliesFeaturesInOrder()
    {
        var module = new OrderedFeatureModule();

        var collection = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(collection, module);

        Verify(module.AppliedOrders.SequenceEqual([1, 5, 10]));
    }

    [VerifyTest]
    public void AddModules_WithEnvironment_PassesEnvironmentToConfiguration()
    {
        var environment = new StubEnvironment("Staging");
        var module = new EnvironmentModule();

        var collection = new ServiceCollection();
        collection.AddModules(environment, module);

        Verify(ReferenceEquals(module.ObservedEnvironment, environment));
    }

    /// <summary>
    /// No environment supplied means the process default, not null.
    /// </summary>
    /// <remarks>
    /// The same environment the [IfEnvironment] attributes are evaluated against. A module that got
    /// null here while its own conditional registrations evaluated against Production would be
    /// looking at two different answers to the same question.
    /// </remarks>
    [VerifyTest]
    public void AddModules_WithoutEnvironment_PassesTheProcessDefaultToConfiguration()
    {
        var module = new EnvironmentModule();

        var collection = new ServiceCollection();
        // Cast is required: without it the call is ambiguous with AddModules(params IDependencyModule[]).
        collection.AddModules((IModuleEnvironment?)null, module);

        // The instance registered into the collection, rather than whatever CreateDefault hands out
        // next — it builds a fresh one per call, so comparing against it would test nothing.
        var registered = Single(
            collection,
            descriptor => descriptor.ServiceType == typeof(IModuleEnvironment)
        );

        Verify(ReferenceEquals(module.ObservedEnvironment, registered.ImplementationInstance));
        Verify(
            module.ObservedEnvironment!.EnvironmentName
                == ModuleEnvironment.CreateDefault().EnvironmentName
        );
        Verify(module.ConfigureCalled);
    }

    [VerifyTest]
    public void AddModules_WithModuleEnvironmentNone_PassesNoneRatherThanTheDefault()
    {
        var module = new EnvironmentModule();

        var collection = new ServiceCollection();
        collection.AddModules(ModuleEnvironment.None, module);

        Verify(ReferenceEquals(module.ObservedEnvironment, ModuleEnvironment.None));
    }

    [VerifyTest]
    public void AddModules_WithEnvironment_RegistersEnvironmentAsSingleton()
    {
        var environment = new StubEnvironment("Production");

        var collection = new ServiceCollection();
        collection.AddModules(environment, new CountingModule());

        var provider = collection.BuildServiceProvider();
        Verify(ReferenceEquals(provider.GetService<IModuleEnvironment>(), environment));
    }

    private class CountingModule : IDependencyModule
    {
        public int ApplyCount { get; private set; }

        public bool LoadModule { get; init; } = true;

        public IDependencyModule[] Children { get; set; } = [];

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public IEnumerable<IDependencyModule> GetModules() => Children;

        public void InternalApplyServices(IServiceCollection serviceCollection) => ApplyCount++;
    }

    private class EquatableModule(string key) : IDependencyModule
    {
        private string Key { get; } = key;

        public int ApplyCount { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void InternalApplyServices(IServiceCollection serviceCollection) => ApplyCount++;

        public override bool Equals(object? obj) =>
            obj is EquatableModule other && other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }

    private class DecoratedThing(IThing inner) : IThing
    {
        public IThing Inner { get; } = inner;
    }

    private class DecoratingModule : IDependencyModule, IServiceCollectionConfiguration
    {
        public bool ConfigureDecoratorsCalled { get; private set; }

        public IReadOnlyList<Type>? ServicesVisibleWhenDecorating { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(IServiceCollection services) { }

        public void ConfigureDecorators(IServiceCollection services)
        {
            ConfigureDecoratorsCalled = true;
            ServicesVisibleWhenDecorating = services
                .Select(descriptor => descriptor.ServiceType)
                .ToList();

            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType != typeof(IThing))
                {
                    continue;
                }

                // Capture before replacing, or the factory closes over its own replacement.
                var inner = services[i];
                services[i] = new ServiceDescriptor(
                    typeof(IThing),
                    provider => new DecoratedThing(
                        (IThing)
                            ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!)
                    ),
                    inner.Lifetime
                );
            }
        }
    }

    private class ConfiguringModule : IDependencyModule, IServiceCollectionConfiguration
    {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton<IThing, Thing>();
    }

    private class FeatureModule
        : IDependencyModule,
            IDependencyModuleApplicatorProvider,
            IServiceCollectionConfiguration
    {
        public bool FeatureApplied { get; private set; }

        public bool FeatureAppliedBeforeConfigure { get; private set; }

        private bool _configured;

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public IEnumerable<IFeatureApplicator> FeatureApplicators()
        {
            yield return new DelegateApplicator(
                0,
                () =>
                {
                    FeatureApplied = true;
                    FeatureAppliedBeforeConfigure = !_configured;
                }
            );
        }

        public void ConfigureServices(IServiceCollection services) => _configured = true;
    }

    private class OrderedFeatureModule : IDependencyModule, IDependencyModuleApplicatorProvider
    {
        public List<int> AppliedOrders { get; } = [];

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public IEnumerable<IFeatureApplicator> FeatureApplicators()
        {
            yield return new DelegateApplicator(10, () => AppliedOrders.Add(10));
            yield return new DelegateApplicator(1, () => AppliedOrders.Add(1));
            yield return new DelegateApplicator(5, () => AppliedOrders.Add(5));
        }
    }

    private class DelegateApplicator(int order, Action onApply) : IFeatureApplicator
    {
        public int Order => order;

        public void Apply(IServiceCollection services, IReadOnlyList<IDependencyModule> modules) =>
            onApply();
    }

    private class EnvironmentModule : IDependencyModule, IEnvironmentServiceCollectionConfiguration
    {
        public IModuleEnvironment? ObservedEnvironment { get; private set; }

        public bool ConfigureCalled { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment)
        {
            ConfigureCalled = true;
            ObservedEnvironment = environment;
        }
    }

    private class StubEnvironment(string name) : IModuleEnvironment
    {
        public string EnvironmentName => name;

        public string? Value(string valueName) => null;
    }
}

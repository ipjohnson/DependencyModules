using System.Collections.Concurrent;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

/// <summary>
/// DependencyRegistry keeps its state in static fields on a generic type, so every test here uses
/// its own marker type to stay isolated from the others.
/// </summary>
public partial class DependencyRegistryTests
{
    private interface IThing;

    private class Thing : IThing;

    private class OtherThing : IThing;

    [VerifyTest]
    public void ApplyServices_RunsRegisteredFunctionsInOrder()
    {
        DependencyRegistry<OrderMarker>.Add(services => services.AddSingleton<IThing, Thing>());
        DependencyRegistry<OrderMarker>.Add(services =>
            services.AddSingleton<IThing, OtherThing>()
        );

        var collection = new ServiceCollection();
        DependencyRegistry<OrderMarker>.ApplyServices(collection);

        Verify(collection.Count == 2);
        Verify(collection[0].ImplementationType == typeof(Thing));
        Verify(collection[1].ImplementationType == typeof(OtherThing));
    }

    private class OrderMarker;

    [VerifyTest]
    public void Registry_IsIsolatedPerTypeArgument()
    {
        DependencyRegistry<IsolationMarkerA>.Add(services =>
            services.AddSingleton<IThing, Thing>()
        );

        var collection = new ServiceCollection();
        DependencyRegistry<IsolationMarkerB>.ApplyServices(collection);

        Verify(collection.Count == 0);
    }

    private class IsolationMarkerA;

    private class IsolationMarkerB;

    [VerifyTest]
    public void Add_WithFactory_RegistersWithRequestedLifetime()
    {
        DependencyRegistry<FactoryMarker>.Add<Thing>(_ => new Thing(), ServiceLifetime.Scoped);

        var collection = new ServiceCollection();
        DependencyRegistry<FactoryMarker>.ApplyServices(collection);

        var descriptor = Single(collection);
        Verify(descriptor.Lifetime == ServiceLifetime.Scoped);
        Verify(descriptor.ServiceType == typeof(Thing));
        Verify(descriptor.ImplementationFactory is not null);
    }

    private class FactoryMarker;

    [VerifyTest]
    public void Add_WithImplementationTypeAndKey_RegistersKeyedService()
    {
        DependencyRegistry<KeyedMarker>.Add<IThing>(
            typeof(Thing),
            ServiceLifetime.Singleton,
            "the-key"
        );

        var collection = new ServiceCollection();
        DependencyRegistry<KeyedMarker>.ApplyServices(collection);

        var descriptor = Single(collection);
        Verify(descriptor.IsKeyedService);
        Verify(Equals(descriptor.ServiceKey, "the-key"));
        Verify(descriptor.Lifetime == ServiceLifetime.Singleton);
        Verify(descriptor.KeyedImplementationType == typeof(Thing));
    }

    private class KeyedMarker;

    /// <summary>
    /// Order controls how decorators nest: lower values are applied first and end up closer to the
    /// implementation. Pipeline behaviours contributed by different packages rely on this, so the
    /// ordering has to hold across every decorator in the registry, not just within one group.
    /// </summary>
    [VerifyTest]
    public void ApplyDecorators_AppliesInAscendingOrder()
    {
        var applied = new List<string>();

        DependencyRegistry<OrderedDecoratorMarker>.AddDecorator(_ => applied.Add("third"), 30);
        DependencyRegistry<OrderedDecoratorMarker>.AddDecorator(_ => applied.Add("first"), 10);
        DependencyRegistry<OrderedDecoratorMarker>.AddDecorator(_ => applied.Add("second"), 20);

        DependencyRegistry<OrderedDecoratorMarker>.ApplyDecorators(new ServiceCollection());

        Verify(applied.SequenceEqual(["first", "second", "third"]));
    }

    private class OrderedDecoratorMarker;

    [VerifyTest]
    public void ApplyDecorators_WithoutAnOrder_AppliesInRegistrationOrder()
    {
        var applied = new List<string>();

        DependencyRegistry<UnorderedDecoratorMarker>.AddDecorator(_ => applied.Add("first"));
        DependencyRegistry<UnorderedDecoratorMarker>.AddDecorator(_ => applied.Add("second"));

        DependencyRegistry<UnorderedDecoratorMarker>.ApplyDecorators(new ServiceCollection());

        Verify(applied.SequenceEqual(["first", "second"]));
    }

    private class UnorderedDecoratorMarker;

    /// <summary>
    /// Equal orders must not nest arbitrarily; the sort has to be stable so the result is
    /// reproducible between runs.
    /// </summary>
    [VerifyTest]
    public void ApplyDecorators_WithEqualOrders_KeepsRegistrationOrder()
    {
        var applied = new List<string>();

        DependencyRegistry<StableDecoratorMarker>.AddDecorator(_ => applied.Add("first"), 5);
        DependencyRegistry<StableDecoratorMarker>.AddDecorator(_ => applied.Add("second"), 5);
        DependencyRegistry<StableDecoratorMarker>.AddDecorator(_ => applied.Add("third"), 5);

        DependencyRegistry<StableDecoratorMarker>.ApplyDecorators(new ServiceCollection());

        Verify(applied.SequenceEqual(["first", "second", "third"]));
    }

    private class StableDecoratorMarker;

    [VerifyTest]
    public void ApplyDecorators_MixesOrderedAndUnorderedRegistrations()
    {
        var applied = new List<string>();

        // An unordered decorator defaults to 0, so a negative order still sits inside it.
        DependencyRegistry<MixedDecoratorMarker>.AddDecorator(_ => applied.Add("default"));
        DependencyRegistry<MixedDecoratorMarker>.AddDecorator(
            _ => applied.Add("application"),
            1000
        );
        DependencyRegistry<MixedDecoratorMarker>.AddDecorator(_ => applied.Add("innermost"), -10);

        DependencyRegistry<MixedDecoratorMarker>.ApplyDecorators(new ServiceCollection());

        Verify(applied.SequenceEqual(["innermost", "default", "application"]));
    }

    private class MixedDecoratorMarker;

    [VerifyTest]
    public void ApplyDecorators_RunsSeparatelyFromServices()
    {
        DependencyRegistry<DecoratorMarker>.Add(services => services.AddSingleton<IThing, Thing>());
        DependencyRegistry<DecoratorMarker>.AddDecorator(services =>
            services.AddSingleton<IThing, OtherThing>()
        );

        var servicesOnly = new ServiceCollection();
        DependencyRegistry<DecoratorMarker>.ApplyServices(servicesOnly);
        Verify(servicesOnly.Count == 1);

        var decoratorsOnly = new ServiceCollection();
        DependencyRegistry<DecoratorMarker>.ApplyDecorators(decoratorsOnly);
        var descriptor = Single(decoratorsOnly);
        Verify(descriptor.ImplementationType == typeof(OtherThing));
    }

    private class DecoratorMarker;

    [VerifyTest]
    public void GetModules_WithNoRegisteredModules_ReturnsSuppliedModules()
    {
        var module = new StubModule();

        var result = DependencyRegistry<GetModulesMarker>.GetModules(module);

        var only = Single(result);
        Verify(ReferenceEquals(only, module));
    }

    [VerifyTest]
    public void GetModules_ConcatenatesRegisteredAndSuppliedModules()
    {
        var registered = new StubModule();
        var supplied = new StubModule();
        DependencyRegistry<GetModulesConcatMarker>.AddModule(registered);

        var result = DependencyRegistry<GetModulesConcatMarker>.GetModules(supplied).ToArray();

        Verify(result.Length == 2);
        Verify(ReferenceEquals(result[0], registered));
        Verify(ReferenceEquals(result[1], supplied));
    }

    private class GetModulesMarker;

    private class GetModulesConcatMarker;

    /// <summary>
    /// Regression test for the thread-safety fix: registration funcs are added from static field
    /// initializers, which the runtime may execute on several threads at once, while another
    /// thread can be enumerating the same list in ApplyServices.
    /// </summary>
    [VerifyTest]
    public void Add_IsSafeUnderConcurrentWritersAndReaders(CancellationToken cancellationToken)
    {
        const int writerCount = 8;
        const int perWriter = 250;

        var failures = new ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);

        var writers = Enumerable
            .Range(0, writerCount)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < perWriter; i++)
                    {
                        DependencyRegistry<ConcurrencyMarker>.Add(services =>
                            services.AddSingleton<IThing, Thing>()
                        );
                    }
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }))
            .ToArray();

        var readers = Enumerable
            .Range(0, 4)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < perWriter; i++)
                    {
                        DependencyRegistry<ConcurrencyMarker>.ApplyServices(
                            new ServiceCollection()
                        );
                    }
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }))
            .ToArray();

        foreach (var thread in writers.Concat(readers))
        {
            thread.Start();
        }

        start.Set();

        foreach (var thread in writers.Concat(readers))
        {
            var finished = thread.Join(TimeSpan.FromSeconds(30));
            Verify(finished, "A registry thread did not finish in time.");
        }

        Verify(failures.IsEmpty);

        var collection = new ServiceCollection();
        DependencyRegistry<ConcurrencyMarker>.ApplyServices(collection);
        Verify(collection.Count == writerCount * perWriter);
    }

    private class ConcurrencyMarker;

    [VerifyTest]
    public void AddModule_IsSafeUnderConcurrentWriters(CancellationToken cancellationToken)
    {
        const int writerCount = 8;
        const int perWriter = 100;

        var failures = new ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);

        var threads = Enumerable
            .Range(0, writerCount)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < perWriter; i++)
                    {
                        DependencyRegistry<ModuleConcurrencyMarker>.AddModule(new StubModule());
                    }
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }))
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        start.Set();

        foreach (var thread in threads)
        {
            var finished = thread.Join(TimeSpan.FromSeconds(30));
            Verify(finished, "A registry thread did not finish in time.");
        }

        Verify(failures.IsEmpty);
        Verify(
            DependencyRegistry<ModuleConcurrencyMarker>.GetModules().Count()
                == writerCount * perWriter
        );
    }

    private class ModuleConcurrencyMarker;

    private class StubModule : IDependencyModule
    {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }

    /// <summary>
    /// The single-argument overloads used to refuse any collection with an IModuleEnvironment in it,
    /// including one registered in the only form they accept, because the guard ran before the
    /// lookup. Every test above passes a collection with no environment at all, which is why it went
    /// unnoticed.
    /// </summary>
    [VerifyTest]
    public void ApplyServices_UsesAnEnvironmentAlreadyInTheCollection()
    {
        DependencyRegistry<SuppliedEnvironmentMarker>.Add(
            (services, environment) =>
            {
                if (environment.EnvironmentName == "Development")
                {
                    services.AddSingleton<IThing, OtherThing>();
                }
            }
        );

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(new StubEnvironment("Development"));

        DependencyRegistry<SuppliedEnvironmentMarker>.ApplyServices(collection);

        Verify(collection.Any(descriptor => descriptor.ImplementationType == typeof(OtherThing)));
    }

    private class SuppliedEnvironmentMarker;

    [VerifyTest]
    public void ApplyDecorators_UsesAnEnvironmentAlreadyInTheCollection()
    {
        var seen = "";

        DependencyRegistry<SuppliedDecoratorEnvironmentMarker>.AddDecorator(
            (EnvironmentRegistryFunc)((_, environment) => seen = environment.EnvironmentName)
        );

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(new StubEnvironment("Staging"));

        DependencyRegistry<SuppliedDecoratorEnvironmentMarker>.ApplyDecorators(collection);

        Verify(seen == "Staging");
    }

    private class SuppliedDecoratorEnvironmentMarker;

    /// <summary>
    /// The guard still has to fire. An environment the container would build rather than hand back
    /// cannot decide registrations, because there is no provider yet to build it with.
    /// </summary>
    [VerifyTest]
    [Row(true)]
    [Row(false)]
    public void ApplyServices_RefusesAnEnvironmentItCannotUse(bool registeredByType)
    {
        var collection = new ServiceCollection();

        if (registeredByType)
        {
            collection.AddSingleton<IModuleEnvironment, DefaultStubEnvironment>();
        }
        else
        {
            collection.AddSingleton<IModuleEnvironment>(_ => new StubEnvironment("Development"));
        }

        ThrowsExactly<InvalidOperationException>(() =>
            DependencyRegistry<RefusedEnvironmentMarker>.ApplyServices(collection)
        );
    }

    private class RefusedEnvironmentMarker;

    private class StubEnvironment(string name) : IModuleEnvironment
    {
        public string EnvironmentName => name;

        public string? Value(string valueName) => null;
    }

    private class DefaultStubEnvironment() : StubEnvironment("Development");
}

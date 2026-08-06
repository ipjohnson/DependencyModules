using System.Collections.Concurrent;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// DependencyRegistry keeps its state in static fields on a generic type, so every test here uses
/// its own marker type to stay isolated from the others.
/// </summary>
public class DependencyRegistryTests {

    private interface IThing;

    private class Thing : IThing;

    private class OtherThing : IThing;

    [Fact]
    public void ApplyServices_RunsRegisteredFunctionsInOrder() {
        DependencyRegistry<OrderMarker>.Add(services => services.AddSingleton<IThing, Thing>());
        DependencyRegistry<OrderMarker>.Add(services => services.AddSingleton<IThing, OtherThing>());

        var collection = new ServiceCollection();
        DependencyRegistry<OrderMarker>.ApplyServices(collection);

        Assert.Equal(2, collection.Count);
        Assert.Equal(typeof(Thing), collection[0].ImplementationType);
        Assert.Equal(typeof(OtherThing), collection[1].ImplementationType);
    }

    private class OrderMarker;

    [Fact]
    public void Registry_IsIsolatedPerTypeArgument() {
        DependencyRegistry<IsolationMarkerA>.Add(services => services.AddSingleton<IThing, Thing>());

        var collection = new ServiceCollection();
        DependencyRegistry<IsolationMarkerB>.ApplyServices(collection);

        Assert.Empty(collection);
    }

    private class IsolationMarkerA;

    private class IsolationMarkerB;

    [Fact]
    public void Add_WithFactory_RegistersWithRequestedLifetime() {
        DependencyRegistry<FactoryMarker>.Add<Thing>(_ => new Thing(), ServiceLifetime.Scoped);

        var collection = new ServiceCollection();
        DependencyRegistry<FactoryMarker>.ApplyServices(collection);

        var descriptor = Assert.Single(collection);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(Thing), descriptor.ServiceType);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    private class FactoryMarker;

    [Fact]
    public void Add_WithImplementationTypeAndKey_RegistersKeyedService() {
        DependencyRegistry<KeyedMarker>.Add<IThing>(typeof(Thing), ServiceLifetime.Singleton, "the-key");

        var collection = new ServiceCollection();
        DependencyRegistry<KeyedMarker>.ApplyServices(collection);

        var descriptor = Assert.Single(collection);
        Assert.True(descriptor.IsKeyedService);
        Assert.Equal("the-key", descriptor.ServiceKey);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(Thing), descriptor.KeyedImplementationType);
    }

    private class KeyedMarker;

    [Fact]
    public void ApplyDecorators_RunsSeparatelyFromServices() {
        DependencyRegistry<DecoratorMarker>.Add(services => services.AddSingleton<IThing, Thing>());
        DependencyRegistry<DecoratorMarker>.AddDecorator(services => services.AddSingleton<IThing, OtherThing>());

        var servicesOnly = new ServiceCollection();
        DependencyRegistry<DecoratorMarker>.ApplyServices(servicesOnly);
        Assert.Single(servicesOnly);

        var decoratorsOnly = new ServiceCollection();
        DependencyRegistry<DecoratorMarker>.ApplyDecorators(decoratorsOnly);
        var descriptor = Assert.Single(decoratorsOnly);
        Assert.Equal(typeof(OtherThing), descriptor.ImplementationType);
    }

    private class DecoratorMarker;

    [Fact]
    public void GetModules_WithNoRegisteredModules_ReturnsSuppliedModules() {
        var module = new StubModule();

        var result = DependencyRegistry<GetModulesMarker>.GetModules(module);

        Assert.Same(module, Assert.Single(result));
    }

    [Fact]
    public void GetModules_ConcatenatesRegisteredAndSuppliedModules() {
        var registered = new StubModule();
        var supplied = new StubModule();
        DependencyRegistry<GetModulesConcatMarker>.AddModule(registered);

        var result = DependencyRegistry<GetModulesConcatMarker>.GetModules(supplied).ToArray();

        Assert.Equal(2, result.Length);
        Assert.Same(registered, result[0]);
        Assert.Same(supplied, result[1]);
    }

    private class GetModulesMarker;

    private class GetModulesConcatMarker;

    /// <summary>
    /// Regression test for the thread-safety fix: registration funcs are added from static field
    /// initializers, which the runtime may execute on several threads at once, while another
    /// thread can be enumerating the same list in ApplyServices.
    /// </summary>
    [Fact]
    public void Add_IsSafeUnderConcurrentWritersAndReaders() {
        const int writerCount = 8;
        const int perWriter = 250;

        var failures = new ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);

        var writers = Enumerable.Range(0, writerCount).Select(_ => new Thread(() => {
            try {
                start.Wait(TestContext.Current.CancellationToken);
                for (var i = 0; i < perWriter; i++) {
                    DependencyRegistry<ConcurrencyMarker>.Add(services => services.AddSingleton<IThing, Thing>());
                }
            }
            catch (Exception e) {
                failures.Add(e);
            }
        })).ToArray();

        var readers = Enumerable.Range(0, 4).Select(_ => new Thread(() => {
            try {
                start.Wait(TestContext.Current.CancellationToken);
                for (var i = 0; i < perWriter; i++) {
                    DependencyRegistry<ConcurrencyMarker>.ApplyServices(new ServiceCollection());
                }
            }
            catch (Exception e) {
                failures.Add(e);
            }
        })).ToArray();

        foreach (var thread in writers.Concat(readers)) {
            thread.Start();
        }

        start.Set();

        foreach (var thread in writers.Concat(readers)) {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A registry thread did not finish in time.");
        }

        Assert.Empty(failures);

        var collection = new ServiceCollection();
        DependencyRegistry<ConcurrencyMarker>.ApplyServices(collection);
        Assert.Equal(writerCount * perWriter, collection.Count);
    }

    private class ConcurrencyMarker;

    [Fact]
    public void AddModule_IsSafeUnderConcurrentWriters() {
        const int writerCount = 8;
        const int perWriter = 100;

        var failures = new ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);

        var threads = Enumerable.Range(0, writerCount).Select(_ => new Thread(() => {
            try {
                start.Wait(TestContext.Current.CancellationToken);
                for (var i = 0; i < perWriter; i++) {
                    DependencyRegistry<ModuleConcurrencyMarker>.AddModule(new StubModule());
                }
            }
            catch (Exception e) {
                failures.Add(e);
            }
        })).ToArray();

        foreach (var thread in threads) {
            thread.Start();
        }

        start.Set();

        foreach (var thread in threads) {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A registry thread did not finish in time.");
        }

        Assert.Empty(failures);
        Assert.Equal(writerCount * perWriter, DependencyRegistry<ModuleConcurrencyMarker>.GetModules().Count());
    }

    private class ModuleConcurrencyMarker;

    private class StubModule : IDependencyModule {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }
}

using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// The AddModule/AddModules overloads are the library's entry point, so each one is covered here.
/// </summary>
public class ServiceCollectionExtensionsTests {

    private interface IThing;

    private class Thing : IThing;

    [Fact]
    public void AddModule_Generic_PopulatesTheCollection() {
        var collection = new ServiceCollection();

        collection.AddModule<RegisteringModule>();

        Assert.Contains(collection, descriptor => descriptor.ServiceType == typeof(IThing));
    }

    [Fact]
    public void AddModule_Generic_ReturnsTheSameCollectionForChaining() {
        var collection = new ServiceCollection();

        var returned = collection.AddModule<RegisteringModule>();

        Assert.Same(collection, returned);
    }

    [Fact]
    public void AddModule_Instance_PopulatesTheCollection() {
        var collection = new ServiceCollection();

        collection.AddModule(new RegisteringModule());

        Assert.Contains(collection, descriptor => descriptor.ServiceType == typeof(IThing));
    }

    [Fact]
    public void AddModule_Instance_ReturnsTheSameCollectionForChaining() {
        var collection = new ServiceCollection();
        var module = new RegisteringModule();

        var returned = collection.AddModule(module);

        Assert.Same(collection, returned);
    }

    [Fact]
    public void AddModules_AppliesEveryModule() {
        var first = new RegisteringModule();
        var second = new OtherRegisteringModule();

        var collection = new ServiceCollection();
        collection.AddModules(first, second);

        Assert.Contains(collection, descriptor => descriptor.ServiceType == typeof(IThing));
        Assert.Contains(collection, descriptor => descriptor.ServiceType == typeof(IOther));
    }

    [Fact]
    public void AddModules_ReturnsTheSameCollectionForChaining() {
        var collection = new ServiceCollection();

        var returned = collection.AddModules(new RegisteringModule());

        Assert.Same(collection, returned);
    }

    [Fact]
    public void AddModules_WithNoModules_LeavesTheCollectionEmpty() {
        var collection = new ServiceCollection();

        collection.AddModules();

        Assert.Empty(collection);
    }

    [Fact]
    public void AddModules_WithEnvironment_ReturnsTheSameCollectionForChaining() {
        var collection = new ServiceCollection();

        var returned = collection.AddModules(new StubEnvironment(), new RegisteringModule());

        Assert.Same(collection, returned);
    }

    [Fact]
    public void AddedServices_ResolveFromTheBuiltProvider() {
        var collection = new ServiceCollection();
        collection.AddModule<RegisteringModule>();

        var provider = collection.BuildServiceProvider();

        Assert.IsType<Thing>(provider.GetService<IThing>());
    }

    private interface IOther;

    private class Other : IOther;

    private class RegisteringModule : IDependencyModule, IServiceCollectionConfiguration {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) =>
            DependencyRegistryTestHelper.LoadSingleModule(serviceCollection, this);

        public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IThing, Thing>();
    }

    private class OtherRegisteringModule : IDependencyModule, IServiceCollectionConfiguration {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) =>
            DependencyRegistryTestHelper.LoadSingleModule(serviceCollection, this);

        public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IOther, Other>();
    }

    private class StubEnvironment : IModuleEnvironment {
        public string EnvironmentName => "Test";

        public string? Value(string name) => null;
    }
}

/// <summary>
/// Generated modules route PopulateServiceCollection through DependencyRegistry; hand-written test
/// modules do the same so they exercise the real code path.
/// </summary>
internal static class DependencyRegistryTestHelper {
    public static void LoadSingleModule(IServiceCollection services, IDependencyModule module) =>
        Runtime.Helpers.DependencyRegistry<object>.LoadModules(services, module);
}

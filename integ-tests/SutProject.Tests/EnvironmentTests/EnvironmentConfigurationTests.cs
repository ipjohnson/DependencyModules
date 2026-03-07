using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.EnvironmentTests;

public class TestEnvironment : IModuleEnvironment {
    public string EnvironmentName { get; }

    private readonly Dictionary<string, string> _values;

    public TestEnvironment(string environmentName, Dictionary<string, string>? values = null) {
        EnvironmentName = environmentName;
        _values = values ?? new Dictionary<string, string>();
    }

    public string? Value(string name) {
        return _values.TryGetValue(name, out var value) ? value : null;
    }
}

public interface IEnvironmentDependency {
    string EnvironmentName { get; }
}

public class EnvironmentDependency(string environmentName) : IEnvironmentDependency {
    public string EnvironmentName { get; } = environmentName;
}

[DependencyModule]
public partial class EnvironmentAwareModule : IEnvironmentServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment? environment) {
        var envName = environment?.EnvironmentName ?? "Unknown";
        services.AddSingleton<IEnvironmentDependency>(new EnvironmentDependency(envName));
    }
}

[DependencyModule]
public partial class DualConfigModule : IServiceCollectionConfiguration, IEnvironmentServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(new StringMarker("from-configure"));
    }

    public void ConfigureServices(IServiceCollection services, IModuleEnvironment? environment) {
        var envName = environment?.EnvironmentName ?? "Unknown";
        services.AddSingleton<IEnvironmentDependency>(new EnvironmentDependency(envName));
    }
}

public class StringMarker(string value) {
    public string Value { get; } = value;
}

public class EnvironmentConfigurationTests {
    [Fact]
    public void EnvironmentPassedToModule_WhenRegistered() {
        var serviceCollection = new ServiceCollection();
        var environment = new TestEnvironment("Production");

        serviceCollection.AddModules(environment, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dependency = serviceProvider.GetRequiredService<IEnvironmentDependency>();

        Assert.Equal("Production", dependency.EnvironmentName);
    }

    [Fact]
    public void NullEnvironment_WhenNotRegistered() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules(new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dependency = serviceProvider.GetRequiredService<IEnvironmentDependency>();

        Assert.Equal("Unknown", dependency.EnvironmentName);
    }

    [Fact]
    public void EnvironmentRegisteredAsSingleton_WhenProvided() {
        var serviceCollection = new ServiceCollection();
        var environment = new TestEnvironment("Development");

        serviceCollection.AddModules(environment, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var resolved = serviceProvider.GetRequiredService<IModuleEnvironment>();

        Assert.Same(environment, resolved);
    }

    [Fact]
    public void EnvironmentValues_AccessibleInModule() {
        var serviceCollection = new ServiceCollection();
        var values = new Dictionary<string, string> {
            { "Region", "us-east-1" },
            { "Feature.NewUI", "true" }
        };
        var environment = new TestEnvironment("Staging", values);

        serviceCollection.AddModules(environment, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var resolved = serviceProvider.GetRequiredService<IModuleEnvironment>();

        Assert.Equal("us-east-1", resolved.Value("Region"));
        Assert.Equal("true", resolved.Value("Feature.NewUI"));
        Assert.Null(resolved.Value("NonExistent"));
    }

    [Fact]
    public void BothConfigurationInterfaces_CalledCorrectly() {
        var serviceCollection = new ServiceCollection();
        var environment = new TestEnvironment("Test");

        serviceCollection.AddModules(environment, new DualConfigModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var envDep = serviceProvider.GetRequiredService<IEnvironmentDependency>();
        var marker = serviceProvider.GetRequiredService<StringMarker>();

        Assert.Equal("Test", envDep.EnvironmentName);
        Assert.Equal("from-configure", marker.Value);
    }

    [Fact]
    public void NullEnvironmentParameter_DoesNotRegisterSingleton() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules((IModuleEnvironment?)null, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var resolved = serviceProvider.GetService<IModuleEnvironment>();

        Assert.Null(resolved);
    }
}

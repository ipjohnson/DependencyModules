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
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) {
        var envName = environment.EnvironmentName;
        services.AddSingleton<IEnvironmentDependency>(new EnvironmentDependency(envName));
    }
}

[DependencyModule]
public partial class DualConfigModule : IServiceCollectionConfiguration, IEnvironmentServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(new StringMarker("from-configure"));
    }

    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) {
        var envName = environment.EnvironmentName;
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

    /// <summary>
    /// No environment supplied means the process default rather than null.
    /// </summary>
    [Fact]
    public void ProcessEnvironment_WhenNotRegistered() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules(new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dependency = serviceProvider.GetRequiredService<IEnvironmentDependency>();

        Assert.Equal(ModuleEnvironment.CreateDefault().EnvironmentName, dependency.EnvironmentName);
    }

    /// <summary>
    /// An application with genuinely no environment says so, and gets a real object rather than a
    /// null to branch on.
    /// </summary>
    [Fact]
    public void ModuleEnvironmentNone_HasNoNameAndNoValues() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules(ModuleEnvironment.None, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dependency = serviceProvider.GetRequiredService<IEnvironmentDependency>();

        Assert.Equal("", dependency.EnvironmentName);
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

    /// <summary>
    /// Nothing supplied registers the process default, so the environment that decided the
    /// registrations is the same one that resolves.
    /// </summary>
    [Fact]
    public void NullEnvironmentParameter_RegistersTheProcessDefault() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules((IModuleEnvironment?)null, new EnvironmentAwareModule());

        // The registered instance is the one that decided the registrations. CreateDefault builds a
        // fresh environment per call, so the invariant is that these are the same object — not that
        // either matches something asked for later.
        var registered = Assert.Single(
            serviceCollection, descriptor => descriptor.ServiceType == typeof(IModuleEnvironment));

        var serviceProvider = serviceCollection.BuildServiceProvider();

        Assert.Same(
            registered.ImplementationInstance, serviceProvider.GetRequiredService<IModuleEnvironment>());
        Assert.Equal(
            ModuleEnvironment.CreateDefault().EnvironmentName,
            serviceProvider.GetRequiredService<IModuleEnvironment>().EnvironmentName);
    }

    /// <summary>
    /// An environment the application supplied is never displaced by the default.
    /// </summary>
    [Fact]
    public void SuppliedEnvironmentIsNotReplacedByTheDefault() {
        var serviceCollection = new ServiceCollection();
        var environment = new TestEnvironment("Staging");

        serviceCollection.AddModules(environment, new EnvironmentAwareModule());

        var serviceProvider = serviceCollection.BuildServiceProvider();

        Assert.Same(environment, serviceProvider.GetRequiredService<IModuleEnvironment>());
        Assert.Single(serviceCollection, d => d.ServiceType == typeof(IModuleEnvironment));
    }
}

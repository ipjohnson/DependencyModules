using DependencyModules.xUnit.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes;

/// <summary>
/// An attribute used for configuring and exporting services to the dependency injection container
/// during xUnit test execution. This attribute supports defining services and their implementations
/// with specific lifetimes for test scenarios.
/// </summary>
/// <remarks>
/// This attribute can be applied to assemblies, classes, or methods to provide granular service
/// configuration for specific testing contexts. It integrates with xUnit by implementing the
/// ITestStartupAttribute, enabling setup and initialization processes within the xUnit testing
/// framework.
/// </remarks>
/// <example>
/// It enables dependency injection for testing by adding services to the service collection
/// using the defined service type, optional implementation type, and a configurable lifetime.
/// </example>
/// <threadsafety>
/// This attribute does not guarantee thread safety and should be used with appropriate considerations
/// in concurrent test scenarios.
/// </threadsafety>
/// <seealso cref="DependencyModules.xUnit.Attributes.Interfaces.ITestStartupAttribute" />
[AttributeUsage(
    AttributeTargets.Assembly |
    AttributeTargets.Class |
    AttributeTargets.Method,
    AllowMultiple = true)]
public class TestExportAttribute : Attribute, ITestStartupAttribute {
    /// <summary>
    /// An attribute that configures and exports services to the dependency injection container
    /// for xUnit test scenarios. This supports customized service registrations with specific lifetimes
    /// for specified testing contexts.
    /// </summary>
    /// <remarks>
    /// This attribute facilitates dependency injection configuration for xUnit tests by allowing
    /// the addition of services and their implementations to the service collection during test execution.
    /// It is applicable to assemblies, classes, or methods, enabling fine-grained control over service
    /// registrations for different testing phases or requirements.
    /// </remarks>
    /// <threadsafety>
    /// This attribute is not guaranteed to be thread-safe and should be used carefully when
    /// dealing with parallel or concurrent test execution.
    /// </threadsafety>
    public TestExportAttribute(Type service) {
        Service = service;
    }

    /// <summary>
    /// Gets the service type to be registered in the service collection.
    /// The specified type represents the service interface or base type for dependency injection.
    /// </summary>
    public Type Service {
        get;
    }

    /// <summary>
    /// Gets or sets the implementation type to be registered for the associated service in the service collection.
    /// If no value is provided, the service type will be used as the implementation type by default.
    /// </summary>
    public Type? Implementation {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets the lifetime of the service in the dependency injection container.
    /// Determines whether the service is registered as singleton, scoped, or transient.
    /// </summary>
    public ServiceLifetime Lifetime {
        get;
        set;
    } = ServiceLifetime.Transient;


    /// <summary>
    /// Configures the service collection for an xUnit test method by adding services with specified lifetimes.
    /// This method enables dynamic service registration during test execution, supporting dependency injection setup.
    /// </summary>
    /// <param name="testMethod">
    /// The xUnit test method for which the service collection is being configured.
    /// This parameter provides context about the test and can be used for conditional service registrations.
    /// </param>
    /// <param name="serviceCollection">
    /// The service collection to which services are added. This collection is used to configure
    /// the dependency injection container for the test's execution environment.
    /// </param>
    public void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection) {
        var implementation = Implementation ?? Service;

        switch (Lifetime) {
            case ServiceLifetime.Singleton:
                serviceCollection.AddSingleton(Service, implementation);
                break;
            case ServiceLifetime.Scoped:
                serviceCollection.AddScoped(Service, implementation);
                break;
            case ServiceLifetime.Transient:
                serviceCollection.AddTransient(Service, implementation);
                break;
        }
    }

    /// <summary>
    /// Asynchronously initializes services and configurations required for a specific xUnit test method
    /// by using the provided test method context and service provider.
    /// </summary>
    /// <param name="testMethod">
    /// The instance of the xUnit test method being executed. It provides context
    /// for the current test, such as metadata and test execution information.
    /// </param>
    /// <param name="serviceProvider">
    /// The service provider used to resolve dependencies and manage the service lifecycle
    /// during the test execution.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous initialization operation. The task completes
    /// when the service initialization and configurations for the test method are finalized.
    /// </returns>
    public Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider) {
        return Task.CompletedTask;
    }
}
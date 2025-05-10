using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Represents an interface for configuring and initializing services within the xUnit testing framework.
/// </summary>
/// <remarks>
/// Implementations of this interface allow services to be added to the dependency injection container
/// and provide mechanisms to perform additional initialization steps required for test execution.
/// </remarks>
/// <threadsafety>
/// Thread safety is not guaranteed for implementations of this interface. Procedures should account for
/// potential issues in concurrent test execution scenarios.
/// </threadsafety>
public interface ITestStartupAttribute {

    /// <summary>
    /// Configures the dependency injection service collection by adding services or modifying the collection
    /// specifically for a test method within the xUnit testing framework.
    /// </summary>
    /// <param name="testMethod">
    /// The test method for which the service collection is being configured. Provides context about the test
    /// being executed.
    /// </param>
    /// <param name="serviceCollection">
    /// The service collection to be configured. This can involve adding, replacing, or removing services
    /// necessary for the test execution.
    /// </param>
    void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection);

    /// <summary>
    /// Asynchronously initializes and configures services required for the execution of a specific test
    /// within the xUnit testing framework.
    /// </summary>
    /// <param name="testMethod">
    /// The test method that is associated with the current initialization process. Provides the necessary
    /// context for configuring the services specific to the test.
    /// </param>
    /// <param name="serviceProvider">
    /// The service provider that offers access to the configured dependency injection container.
    /// It enables the retrieval of services for initialization or further setup.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation of initializing and configuring the services.
    /// </returns>
    Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider);
}
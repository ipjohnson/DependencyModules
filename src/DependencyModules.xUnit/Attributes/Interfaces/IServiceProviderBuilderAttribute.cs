using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Represents an interface for defining a mechanism to build an <see cref="IServiceProvider"/>
/// within the context of a test method in the xUnit testing framework.
/// Implementations of this interface can be used to customize the service provider for a specific test case,
/// providing test dependencies by configuring the <see cref="IServiceCollection"/>.
/// </summary>
public interface IServiceProviderBuilderAttribute {

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> based on the provided <see cref="IServiceCollection"/> and optional attributes.
    /// This method checks for custom attributes implementing <see cref="IServiceProviderBuilderAttribute"/> to allow
    /// test-specific service provider customization. If no applicable attribute is found, the default
    /// service provider is built using the given service collection.
    /// </summary>
    /// <param name="serviceCollection">
    /// The <see cref="IServiceCollection"/> instance containing service registrations to be used for building the service provider.
    /// </param>
    /// <param name="testCaseContext">
    /// the test case context for the test
    /// <see cref="IXunitTestMethod"/> to customize the service provider construction.
    /// </param>
    /// <returns>
    /// An instance of <see cref="IServiceProvider"/> configured with the services from the given <see cref="IServiceCollection"/>,
    /// optionally customized by a <see cref="IServiceProviderBuilderAttribute"/>.
    /// </returns>
    IServiceProvider BuildServiceProvider(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection);
}
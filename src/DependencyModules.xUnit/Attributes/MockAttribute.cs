using System.Reflection;
using DependencyModules.xUnit.Attributes.Interfaces;
using DependencyModules.xUnit.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes;

/// <summary>
/// Provides mocking capabilities for dependency injection within test methods.
/// </summary>
/// <remarks>
/// This attribute is applied to test method parameters to specify that a mock implementation of the parameter type should
/// be created and registered within the IoC container for the test's execution context. The creation of mock instances
/// is delegated to the mock library supporting the test framework, which must implement <see cref="IMockSupportAttribute"/>.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter,
    AllowMultiple = true)]
public class MockAttribute : Attribute, ITestParameterValueProvider {

    /// <summary>
    /// Configures a service collection with necessary dependencies for the test case context.
    /// </summary>
    /// <param name="testCaseContext">
    /// The xUnit test method context providing access to test-related information and behavior.
    /// </param>
    /// <param name="serviceCollection">
    /// The service collection to configure with services and dependencies required for the test.
    /// </param>
    /// <param name="parameter">
    /// The parameter information representing the type and metadata of the test parameter.
    /// </param>
    /// <exception cref="Exception">
    /// Thrown when a required mock library is not found, indicating that the type or assembly is not correctly attributed.
    /// </exception>
    public void SetupServiceCollection(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection, ParameterInfo parameter) {
        var mockAttribute = testCaseContext.Method.GetTestAttribute<IMockSupportAttribute>();

        if (mockAttribute == null) {
            throw new Exception("Mock library not found, please ensure the Type or Assembly is attributed correctly.");
        }

        var mockedValue = mockAttribute.ProvideMock(parameter.ParameterType);

        serviceCollection.AddSingleton(parameter.ParameterType, _ => mockedValue);
    }

    /// <summary>
    /// Retrieves the parameter value asynchronously using the provided context, service provider, and parameter information.
    /// </summary>
    /// <param name="context">
    /// The test method execution context containing metadata and runtime details of the test case.
    /// </param>
    /// <param name="serviceProvider">
    /// The service provider instance used for resolving the dependency represented by the parameter.
    /// </param>
    /// <param name="parameter">
    /// The parameter information representing the type and metadata of the dependency whose value is being resolved.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the resolved value of the parameter,
    /// or null if the parameter could not be resolved.
    /// </returns>
    public Task<object?> GetParameterValueAsync(IXunitTestMethod context, IServiceProvider serviceProvider, ParameterInfo parameter) {
        return Task.FromResult(serviceProvider.GetService(parameter.ParameterType));
    }
}
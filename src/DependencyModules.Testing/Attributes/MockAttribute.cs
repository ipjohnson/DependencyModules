using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Attributes;

/// <summary>
/// Replaces a test parameter's service with a test double.
/// </summary>
/// <remarks>
/// Applied to a test method parameter. The double is registered in the test's container before
/// anything is resolved, so everything constructed afterwards — the service under test included —
/// is built against it rather than against the real registration.
///
/// Creating the double is delegated to whichever mocking package is in scope, which must implement
/// <see cref="IMockSupportAttribute"/>: <c>[NSubstituteSupport]</c>, <c>[MoqSupport]</c> or
/// <c>[FakeItEasySupport]</c>. Without one this throws rather than silently handing back a real
/// service.
///
/// A library that separates the double from the object it produces may let the parameter name either.
/// With Moq, <c>[Mock] IFoo</c> gives the object and <c>Mock&lt;IFoo&gt;</c> gives the mock — and on a
/// <c>Mock&lt;IFoo&gt;</c> parameter this attribute is redundant, since the type already says what it
/// is.
///
/// This carries no test framework dependency, so it is the same attribute whichever integration
/// resolves the test's parameters.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter,
    AllowMultiple = true)]
public class MockAttribute : Attribute, ITestParameterValueProvider {

    /// <summary>
    /// Registers the double in place of the parameter's service.
    /// </summary>
    /// <param name="testMethod">
    /// The test method context providing access to test-related information and behavior.
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
    public void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection, ParameterInfo parameter) {
        var mockAttribute = testMethod.Method.GetTestAttribute<IMockSupportAttribute>();

        if (mockAttribute == null) {
            throw new Exception("Mock library not found, please ensure the Type or Assembly is attributed correctly.");
        }

        var mockedValue = mockAttribute.ProvideMock(parameter.ParameterType);

        serviceCollection.AddSingleton(parameter.ParameterType, _ => mockedValue);
    }

    /// <summary>
    /// Retrieves the parameter value asynchronously using the provided context, service provider, and parameter information.
    /// </summary>
    /// <param name="testMethod">
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
    public Task<object?> GetParameterValueAsync(
        ITestMethodContext testMethod, IServiceProvider serviceProvider, ParameterInfo parameter) {
        return Task.FromResult(serviceProvider.GetService(parameter.ParameterType));
    }
}

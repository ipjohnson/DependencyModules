using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Interface that defines the contract for providing custom parameter values for test methods in xUnit.
/// </summary>
/// <remarks>
/// Implementations of this interface can modify the dependency injection service collection for a test method's
/// execution context and provide specific values for method parameters at runtime. This enables customization of
/// test cases by dynamically resolving parameter dependencies or injecting mock/fake objects.
/// </remarks>
public interface ITestParameterValueProvider {
    /// <summary>
    /// Configures the dependency injection service collection for a test method's execution context.
    /// </summary>
    /// <param name="testCaseContext">
    /// An <see cref="IXunitTestMethod"/> instance that represents the test method's execution context.
    /// This provides metadata about the test method, including its attributes and parameters.
    /// </param>
    /// <param name="serviceCollection">
    /// An instance of <see cref="IServiceCollection"/> that defines the service collection for the test execution.
    /// Services can be added or modified within this method.
    /// </param>
    /// <param name="parameter">
    /// A <see cref="ParameterInfo"/> object representing the specific parameter of the test method
    /// for which the dependency injection setup is being customized.
    /// This allows fine-grained control over services related to specific parameters.
    /// </param>
    void SetupServiceCollection(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection, ParameterInfo parameter);

    /// <summary>
    /// Asynchronously retrieves the value of a test method parameter at runtime based on the provided context,
    /// service provider, and parameter metadata.
    /// </summary>
    /// <param name="context">
    /// An instance of <see cref="IXunitTestMethod"/> representing the context of the test method execution.
    /// This provides information such as the test method's attributes, signatures, and parameters.
    /// </param>
    /// <param name="serviceProvider">
    /// An instance of <see cref="IServiceProvider"/> used to resolve services or dependencies
    /// required for the parameter value generation.
    /// </param>
    /// <param name="parameter">
    /// An instance of <see cref="ParameterInfo"/> that contains metadata about the parameter
    /// for which the value is to be retrieved.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that represents the asynchronous operation.
    /// The result contains the parameter value, or <c>null</c> if no value could be resolved.
    /// </returns>
    Task<object?> GetParameterValueAsync(IXunitTestMethod context, IServiceProvider serviceProvider, ParameterInfo parameter);
}
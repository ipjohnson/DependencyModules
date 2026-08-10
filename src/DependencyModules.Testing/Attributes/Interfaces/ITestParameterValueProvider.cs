using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Supplies the value for a single test method parameter, along with any services that value depends
/// on.
/// </summary>
/// <remarks>
/// Applies to the parameter itself rather than being found by walking the attribute chain, so it
/// speaks for one parameter only. <c>[Mock]</c> is the canonical implementation.
///
/// The two halves run at different points, and the gap between them is the whole point: the setup
/// runs before the container is built, so a parameter can change what the service under test is
/// constructed with, not merely what the test itself ends up holding.
/// </remarks>
public interface ITestParameterValueProvider {

    /// <summary>
    /// Adds whatever services are needed to supply this parameter.
    /// </summary>
    /// <param name="testMethod">The test the container is being built for.</param>
    /// <param name="serviceCollection">The collection backing the test's container.</param>
    /// <param name="parameter">The parameter being supplied.</param>
    void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection, ParameterInfo parameter);

    /// <summary>
    /// Produces the value to pass for this parameter.
    /// </summary>
    /// <param name="testMethod">The test being run.</param>
    /// <param name="serviceProvider">The test's container, fully built.</param>
    /// <param name="parameter">The parameter being supplied.</param>
    /// <returns>
    /// The value, or null to stand aside — the next provider on the parameter is tried, and failing
    /// that the parameter is resolved from the container like any other.
    /// </returns>
    Task<object?> GetParameterValueAsync(
        ITestMethodContext testMethod, IServiceProvider serviceProvider, ParameterInfo parameter);
}

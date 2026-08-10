using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Contributes service registrations to a test's container, with the test method in hand.
/// </summary>
/// <remarks>
/// Applies to a method, a class or an assembly and is found by walking that chain, so a registration
/// every test needs can be declared once in an <c>AssemblyInfo</c>.
///
/// Because the method is supplied, an implementation can register based on what the test actually
/// asked for rather than only on how the attribute was configured. <c>DependencyModules.Moq</c> uses
/// this to spot a <c>Mock&lt;T&gt;</c> parameter and register both the mock and the object it
/// produces, so the test holds the one and the service under test is built against the other.
///
/// Runs before the container is built, in widest-scope-first order. Registrations are last-one-wins,
/// so an attribute on the method overrides the same service registered from the assembly.
/// </remarks>
public interface ITestServiceSetupAttribute {

    /// <summary>
    /// Adds services for the given test.
    /// </summary>
    /// <param name="testMethod">The test the container is being built for.</param>
    /// <param name="serviceCollection">The collection backing the test's container.</param>
    void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection);
}

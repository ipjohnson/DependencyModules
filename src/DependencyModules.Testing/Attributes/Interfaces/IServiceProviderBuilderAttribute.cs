using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Replaces the container a test runs against.
/// </summary>
/// <remarks>
/// Only one is used, unlike the other hooks, which all contribute. The narrowest declaration wins:
/// one on the method beats one on the class, which beats one on the assembly — so a broad default
/// can be set at assembly level and overridden by the odd test that needs a different container.
/// Without one the collection is built with <c>BuildServiceProvider()</c>.
///
/// Implement this to hand the test a third-party container, or to build the default one with options
/// it would not otherwise get, such as scope validation. It runs last, after every other hook has
/// contributed, so it is also the final chance to inspect or amend the collection.
/// </remarks>
public interface IServiceProviderBuilderAttribute {

    /// <summary>
    /// Builds the container for the test.
    /// </summary>
    /// <param name="testMethod">The test the container is being built for.</param>
    /// <param name="serviceCollection">The fully populated collection.</param>
    /// <returns>The container the test resolves its parameters and services from.</returns>
    IServiceProvider BuildServiceProvider(ITestMethodContext testMethod, IServiceCollection serviceCollection);
}

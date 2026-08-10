namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Runs against a test's container once it has been built, before the test method is invoked.
/// </summary>
/// <remarks>
/// Separate from <see cref="ITestServiceSetupAttribute"/> because the two happen at different points
/// and most attributes want only one of them: registering services needs a collection and no
/// provider, while seeding state needs a provider and is too late to change registrations. An
/// attribute that genuinely does both implements both.
///
/// Applies to a method, a class or an assembly, and is found by walking that chain.
/// </remarks>
public interface ITestStartupAttribute {

    /// <summary>
    /// Performs whatever asynchronous setup the test needs — seeding a store, opening a connection,
    /// priming a cache.
    /// </summary>
    /// <param name="testMethod">The test being prepared.</param>
    /// <param name="serviceProvider">The test's container, fully built.</param>
    /// <returns>A task that completes when the test is ready to run.</returns>
    Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider);
}

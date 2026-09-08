namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Builds containers for one test, on demand, from what the test composed.
/// </summary>
/// <remarks>
/// <para>
/// Resolved from the test's container like any other service, so anything driving the application -
/// a trigger façade, a generated client, a host adapter - can take one and build a container per
/// call. A test that wants those calls to share one container marks that parameter
/// <see cref="SharedAttribute"/>, and the parameter is resolved once instead.
/// </para>
/// <para>
/// <b>A source rather than the <c>IServiceCollection</c>.</b> A caller holding the collection could
/// mutate it, after which the third container differs from the first with nothing recording why.
/// Handing out a source also means every container a test built is tracked, so destroying them is
/// the runner's business rather than a caller's - they are disposed when the test case has run,
/// alongside the container the test itself resolved from.
/// </para>
/// <para>
/// Asynchronous because <see cref="ITestStartupAttribute"/> is, and a container that skipped startup
/// is not the one the test composed. A framework whose startup installs middleware would answer every
/// request through a chain that was never assembled.
/// </para>
/// </remarks>
public interface ITestContainerSource {

    /// <summary>
    /// A container built from the test's composition, started, and owned by the runner.
    /// </summary>
    /// <remarks>
    /// Pinned services are the same objects in every container this returns. Everything else is built
    /// again, keeping whatever lifetime it was registered with inside the container it belongs to.
    /// </remarks>
    ValueTask<IServiceProvider> CreateAsync();
}

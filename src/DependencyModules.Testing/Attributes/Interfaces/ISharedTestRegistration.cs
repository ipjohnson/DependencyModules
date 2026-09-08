namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Declares that what an attribute registered is pinned for the whole test, rather than rebuilt
/// with every container the test creates.
/// </summary>
/// <remarks>
/// <para>
/// A test that builds a container per invocation needs some things to survive the rebuild. A mock is
/// the clear case: a substitute resolved fresh per container is one the test can assert nothing
/// about, because the invocation recorded onto a different object. Implementing this is how an
/// attribute says so once, in its own definition, rather than every use site remembering
/// <c>[Shared]</c>.
/// </para>
/// <para>
/// <b>The bar is narrow.</b> Implement this only where isolated would be <em>broken</em> for the
/// attribute rather than merely unusual. Hiding a decision from the reader of a test is a cost;
/// hiding a non-decision is not. <see cref="MockAttribute"/> clears the bar because an isolated mock
/// has no coherent reading at all. <see cref="TestExportAttribute"/> does not, which is why it
/// implements this with <c>Shared</c> defaulting to false and leaves the choice at the use site.
/// </para>
/// </remarks>
public interface ISharedTestRegistration {

    /// <summary>
    /// Whether what this attribute registered is pinned.
    /// </summary>
    /// <remarks>
    /// A <c>bool</c> rather than a bare marker interface, so an attribute whose answer depends on how
    /// it was constructed can say so. The default suits an attribute that is always shared.
    /// </remarks>
    bool Shared => true;

    /// <summary>
    /// The services to pin, for an attribute that registers one without naming a parameter.
    /// </summary>
    /// <remarks>
    /// Empty means the harness already knows what to pin, which is the parameter's type for an
    /// attribute sitting on one. An <see cref="ITestServiceSetupAttribute"/> has no parameter to read,
    /// so it answers here - which is also what lets an implementation outside this assembly join in
    /// without the runner knowing about it.
    /// </remarks>
    IReadOnlyList<Type> SharedServices => [];
}

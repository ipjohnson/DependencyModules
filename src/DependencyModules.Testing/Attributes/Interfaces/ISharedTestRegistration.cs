using System.Reflection;
namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Adjusts which objects survive a test's container being rebuilt.
/// </summary>
/// <remarks>
/// <para>
/// The default needs no attribute, and is two sentences:
/// </para>
/// <para>
/// <b>A test parameter is one instance for the whole test, unless it is something the harness
/// supplies to drive the application. A registration nothing holds is per container, unless the
/// harness pins it by name.</b>
/// </para>
/// <para>
/// A parameter exists to be looked at - the test was handed it so it could assert on it, or
/// configure it and then assert on something else - so handing the test one container's instance
/// while the invocation runs against another makes the assertion meaningless. That is true of a
/// <c>[Mock]</c>, of a plain application class a handler appends to, and of a registry a test
/// registers a filter on, and no attribute is what makes it so.
/// </para>
/// <para>
/// <b>This interface is for the two exceptions.</b> <see cref="IsolatedServices"/> names the
/// parameters that must <em>not</em> be pinned because they build containers rather than live in
/// one, and <see cref="SharedServices"/> names services no parameter holds that should be kept
/// anyway. <see cref="Shared"/> answering false declines the default for one parameter.
/// </para>
/// <para>
/// An earlier version of this pinned only what an attribute declared. It failed on the first thing
/// a new user runs: a scaffolded test taking a plain application class and asserting on what the
/// handler recorded, which was rebuilt with the container the send ran on. A rule that needs an
/// attribute to work is a rule most tests will not get.
/// </para>
/// </remarks>
public interface ISharedTestRegistration {

    /// <summary>
    /// Whether what this attribute registered is pinned.
    /// </summary>
    /// <remarks>
    /// A <c>bool</c> rather than a bare marker interface, so an attribute whose answer depends on how
    /// it was constructed can say so. On a parameter attribute this is only worth answering to
    /// decline, since a parameter is pinned without being asked.
    /// </remarks>
    bool Shared => true;

    /// <summary>
    /// Services to pin that no test parameter holds.
    /// </summary>
    /// <remarks>
    /// The second exception. A parameter is pinned because the test holds it; something nothing holds
    /// needs naming, which is how a harness keeps its own per-test services - a cancellation token, an
    /// environment - one object across every container, and how
    /// <c>[TestExport(Shared = true)]</c> keeps a fake a handler resolves and the test never sees.
    /// </remarks>
    IReadOnlyList<Type> SharedServices => [];

    /// <summary>
    /// Parameters this attribute supplies that must <em>not</em> be pinned, because they build
    /// containers rather than live in one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exception to the rule that a test parameter is one instance. A trigger façade, a test web
    /// application, an <c>HttpClient</c> and a generated client are handed to a test so it can drive
    /// the application, and each builds a container per call - so pinning one would pin the very
    /// thing that is supposed to be rebuilt, and every call would run against one container while
    /// the test believed otherwise.
    /// </para>
    /// <para>
    /// Named by the attribute that supplies them, because nothing else can know. They are ordinary
    /// types in an ordinary signature, indistinguishable from a service the application registered
    /// until you know which harness put them there.
    /// </para>
    /// <para>
    /// Takes the test method, unlike <see cref="SharedServices"/>, because the answer is a property
    /// of the signature: an attribute supplies a façade for <em>this</em> test's parameters and has
    /// no fixed list of its own.
    /// </para>
    /// <para>
    /// This wins over every other answer here, including an explicit <c>[Shared]</c>. Pinning one of
    /// these is not a preference that could go either way; it silently turns the isolation off.
    /// </para>
    /// </remarks>
    /// <param name="testMethod">The test whose parameters are being supplied.</param>
    IReadOnlyList<Type> IsolatedServices(MethodInfo testMethod) => [];
}

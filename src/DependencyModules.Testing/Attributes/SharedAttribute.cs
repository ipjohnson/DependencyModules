using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Attributes;

/// <summary>
/// Keeps one test parameter across every container the test builds.
/// </summary>
/// <remarks>
/// <para>
/// One rule, covering both things a parameter can be: <b>this parameter is not rebuilt.</b>
/// </para>
/// <para>
/// On a <em>value</em> parameter - a fake, a store, anything the test asserts against - that pins the
/// instance, so every container is handed the same object.
/// </para>
/// <para>
/// On an <em>invoker</em> parameter - something that drives the application, holding an
/// <see cref="ITestContainerSource"/> to build a container per call - it pins the container instead,
/// so every call through that parameter reaches the same one. That is the escape hatch for a test
/// whose subject <em>is</em> the reuse: a response cache serving the second request, a rate limiter
/// tripping on the eleventh, a connection held open.
/// </para>
/// <para>
/// Parameters only, deliberately. At a class or an assembly the obvious reading is "one container
/// across the tests here", which would undo the per-test isolation that already holds and that
/// nothing has ever asked to change.
/// </para>
/// <para>
/// On a runner or a host that builds one container anyway this is a no-op rather than an error. It
/// still says why the test is written the way it is, and the same test may be run somewhere that
/// rebuilds.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public class SharedAttribute : Attribute, ISharedTestRegistration { }

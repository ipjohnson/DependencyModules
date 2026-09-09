using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// Works out which services survive a test's container being rebuilt.
/// </summary>
/// <remarks>
/// Shared by every integration, because the rule is the same one wherever the test runs and only the
/// discovery around it differs.
/// </remarks>
public static class SharedRegistrations {

    /// <summary>
    /// Types the harness itself can never pin, whatever anything else says.
    /// </summary>
    /// <remarks>
    /// <see cref="IServiceProvider"/> is the container. A test asking for one is asking which
    /// container it is in, which is the one question pinning cannot answer.
    /// </remarks>
    private static readonly Type[] NeverPinned = [typeof(IServiceProvider)];

    /// <summary>
    /// The service types to keep across every container the test builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule, in the order it is applied:
    /// </para>
    /// <para>
    /// <b>Every parameter the test is handed is pinned.</b> A parameter exists to be looked at, so
    /// handing the test one container's instance while the invocation runs against another makes the
    /// assertion meaningless. That is as true of a plain application class the handler appends to as
    /// it is of a <c>[Mock]</c>, which is why no attribute is required and why an earlier version of
    /// this that required one failed on the first test a scaffolded project runs.
    /// </para>
    /// <para>
    /// <b>Except the ones that drive the application.</b> A façade, a test web application, an
    /// <c>HttpClient</c> and a generated client build a container per call, so pinning one pins the
    /// thing meant to be rebuilt. Only the harness that supplies them knows which they are, so it
    /// names them through <see cref="ISharedTestRegistration.IsolatedServices"/>, and that answer
    /// wins over everything else here - it is correctness rather than preference.
    /// </para>
    /// <para>
    /// <b>Plus what no parameter holds but something asked for.</b> A harness's own per-test
    /// services, and <c>[TestExport(Shared = true)]</c>.
    /// </para>
    /// <para>
    /// A type nothing registered can end up in this set - a value from a data row, a concrete class
    /// the resolver constructs - and that is inert rather than wrong. Pinning rewrites descriptors,
    /// and there are none to rewrite.
    /// </para>
    /// </remarks>
    /// <param name="method">The test method, for its parameters.</param>
    /// <param name="knownAttributes">
    /// The attributes in scope for the test, widest first, as the runner collected them.
    /// </param>
    public static IReadOnlyCollection<Type> Collect(MethodInfo method, IEnumerable<Attribute> knownAttributes) {
        var attributes = knownAttributes as IReadOnlyCollection<Attribute> ?? knownAttributes.ToArray();

        var isolated = new HashSet<Type>(NeverPinned);

        foreach (var registration in attributes.OfType<ISharedTestRegistration>()) {
            foreach (var service in registration.IsolatedServices(method)) {
                isolated.Add(service);
            }
        }

        var pinned = new HashSet<Type>();

        foreach (var parameter in method.GetParameters()) {
            var declarations = parameter.GetCustomAttributes()
                .OfType<ISharedTestRegistration>()
                .ToArray();

            // Declining is the only thing worth saying on a parameter, since one is pinned without
            // being asked. A second attribute asking cannot undo it: two attributes on one parameter
            // disagreeing is a use site asking for both, and pinned is the answer that leaves the
            // test able to see what it was asserting on.
            if (declarations.Any(declaration => !declaration.Shared)) {
                continue;
            }

            pinned.Add(parameter.ParameterType);

            foreach (var declaration in declarations) {
                foreach (var service in declaration.SharedServices) {
                    pinned.Add(service);
                }
            }
        }

        foreach (var registration in attributes.OfType<ISharedTestRegistration>()) {
            if (!registration.Shared) {
                continue;
            }

            foreach (var service in registration.SharedServices) {
                pinned.Add(service);
            }
        }

        pinned.ExceptWith(isolated);

        return pinned;
    }
}

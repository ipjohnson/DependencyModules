using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// Works out which services are pinned for one test.
/// </summary>
/// <remarks>
/// Shared by every integration, because the rule is the same one wherever the test runs and only the
/// discovery around it differs.
/// </remarks>
public static class SharedRegistrations {

    /// <summary>
    /// The service types to keep across every container the test builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sources, and the difference is only in how each names what it registered. An attribute on
    /// the method, the class or the assembly registers a service without naming a parameter, so it
    /// answers <see cref="ISharedTestRegistration.SharedServices"/> - <c>[TestExport]</c> names the
    /// service it exported. An attribute on a parameter has one by definition, so an empty
    /// <c>SharedServices</c> is read as that parameter's type, which is what keeps <c>[Mock]</c> to a
    /// single interface on the class and nothing else.
    /// </para>
    /// <para>
    /// An attribute answering <c>Shared</c> false contributes nothing rather than un-pinning what
    /// something else pinned. Two attributes naming one service disagreeing is a use site asking for
    /// both, and pinning is the answer that leaves the test able to see what it asked to see.
    /// </para>
    /// </remarks>
    /// <param name="method">The test method, for its parameters.</param>
    /// <param name="knownAttributes">
    /// The attributes in scope for the test, widest first, as the runner collected them.
    /// </param>
    public static IReadOnlyCollection<Type> Collect(MethodInfo method, IEnumerable<Attribute> knownAttributes) {
        var pinned = new HashSet<Type>();

        foreach (var registration in knownAttributes.OfType<ISharedTestRegistration>()) {
            if (!registration.Shared) {
                continue;
            }

            foreach (var service in registration.SharedServices) {
                pinned.Add(service);
            }
        }

        foreach (var parameter in method.GetParameters()) {
            foreach (var registration in parameter.GetCustomAttributes().OfType<ISharedTestRegistration>()) {
                if (!registration.Shared) {
                    continue;
                }

                if (registration.SharedServices.Count == 0) {
                    pinned.Add(parameter.ParameterType);

                    continue;
                }

                foreach (var service in registration.SharedServices) {
                    pinned.Add(service);
                }
            }
        }

        return pinned;
    }
}

using System.Reflection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     An attribute that names the environment a test's container is built for, before any module
///     is applied.
/// </summary>
/// <remarks>
///     <para>
///     Module registrations are conditioned as they are applied: <c>[IfEnvironment]</c> and its
///     siblings are answered from the <see cref="IModuleEnvironment" /> already in the collection,
///     or from a process default when there is none. The test integrations run their service-setup
///     pass after the modules by design - a test registration beats an application one - so an
///     environment registered there arrives after every condition has been decided against the
///     default, and a test had no way to put itself under the environment it declares. The
///     integrations consult this before loading any module, which is the whole difference.
///     </para>
///     <para>
///     The parameter is a <see cref="MethodInfo" /> rather than a test-method context, because
///     this interface is in Runtime and the test-method context is in the Testing package, which
///     references Runtime.
///     </para>
///     <para>
///     Attributes are consulted widest scope first - assembly, then class, then method - and the
///     narrowest one that answers decides, matching how every other attribute resolves.
///     </para>
/// </remarks>
public interface IModuleEnvironmentProvider
{
    /// <summary>
    ///     The environment module conditions are evaluated against, or null to leave the decision
    ///     to a wider scope.
    /// </summary>
    /// <param name="testMethod">The test method the container is being built for.</param>
    IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod);
}

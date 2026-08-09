using DependencyModules.Testing.Attributes.Interfaces;
using NSub = NSubstitute;

namespace DependencyModules.NSubstitute;

/// <summary>
/// Resolves any dependency that is not registered as an NSubstitute substitute.
/// </summary>
/// <remarks>
/// Applies to a method, a class, or a whole assembly, so a test project can switch substitutes on
/// once in an <c>AssemblyInfo</c> rather than per test.
///
/// The substitute is both what gets injected and what you configure, so a parameter marked
/// <c>[Mock]</c> can be set up directly.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest]
/// [NSubstituteSupport]
/// public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
///     log.Received().Write(Arg.Any&lt;string&gt;());
/// }
/// </code>
/// </example>
/// <seealso cref="NSub.Substitute"/>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class NSubstituteSupportAttribute : Attribute, IMockSupportAttribute {

    /// <summary>
    /// Provides a substitute for the specified type.
    /// </summary>
    /// <param name="type">The type to substitute for.</param>
    /// <returns>A substitute implementing <paramref name="type"/>.</returns>
    public object ProvideMock(Type type) {
        return NSub.Substitute.For([type], []);
    }
}

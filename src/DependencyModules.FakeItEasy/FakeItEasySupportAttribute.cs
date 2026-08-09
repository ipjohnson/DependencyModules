using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.FakeItEasy;

/// <summary>
/// Resolves any dependency that is not registered as a FakeItEasy fake.
/// </summary>
/// <remarks>
/// Applies to a method, a class, or a whole assembly, so a test project can switch fakes on once in
/// an <c>AssemblyInfo</c> rather than per test.
///
/// The fake is both what gets injected and what you configure, so a parameter marked <c>[Mock]</c>
/// can be set up with <c>A.CallTo</c> directly.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest]
/// [FakeItEasySupport]
/// public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
///     A.CallTo(() => log.Write(A&lt;string&gt;._)).MustHaveHappened();
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class FakeItEasySupportAttribute : Attribute, IMockSupportAttribute {

    /// <summary>
    /// Provides a fake of the specified type.
    /// </summary>
    /// <remarks>
    /// Built through <c>FakeItEasy.Sdk.Create</c> rather than <c>A.Fake&lt;T&gt;()</c>, which is the
    /// form that takes a <see cref="Type"/> — the type is not known until the test asks for it.
    /// </remarks>
    /// <param name="type">The type to fake.</param>
    /// <returns>A fake implementing <paramref name="type"/>.</returns>
    public object ProvideMock(Type type) {
        return global::FakeItEasy.Sdk.Create.Fake(type);
    }
}

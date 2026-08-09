using DependencyModules.Testing.Attributes.Interfaces;
using MoqLib = Moq;

namespace DependencyModules.Moq;

/// <summary>
/// Resolves any dependency that is not registered as a Moq mock.
/// </summary>
/// <remarks>
/// Applies to a method, a class, or a whole assembly, so a test project can switch mocks on once in
/// an <c>AssemblyInfo</c> rather than per test.
///
/// Moq separates the mock from the object it produces, and the container needs the object, so that
/// is what is injected. Reach the <c>Mock&lt;T&gt;</c> to configure or verify it with
/// <c>Mock.Get(instance)</c>. This is the one place Moq reads differently from NSubstitute and
/// FakeItEasy, where the injected instance is itself the thing you configure.
///
/// Mocks are loose, matching Moq's own default: an unconfigured member returns default rather than
/// throwing.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest]
/// [MoqSupport]
/// public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
///     Mock.Get(log).Verify(x => x.Write(It.IsAny&lt;string&gt;()));
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class MoqSupportAttribute : Attribute, IMockSupportAttribute {

    /// <summary>
    /// Provides a mocked instance of the specified type.
    /// </summary>
    /// <remarks>
    /// Constructed through the closed generic because <c>Mock&lt;T&gt;</c> is the only form Moq
    /// offers, and the type is not known until the test asks for it.
    /// </remarks>
    /// <param name="type">The type to mock.</param>
    /// <returns>
    /// The mocked instance — <c>Mock&lt;T&gt;.Object</c>, not the <c>Mock&lt;T&gt;</c> itself.
    /// </returns>
    public object ProvideMock(Type type) {
        var mock = (MoqLib.Mock)Activator.CreateInstance(typeof(MoqLib.Mock<>).MakeGenericType(type))!;

        return mock.Object;
    }
}

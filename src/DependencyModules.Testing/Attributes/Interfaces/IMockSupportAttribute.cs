namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Defines an interface that provides support for creating mock objects within test contexts.
/// </summary>
/// <remarks>
/// Implementations of this interface are responsible for supplying mock instances
/// for specified types. It is typically used in conjunction with dependency injection
/// to enable mocking capabilities in testing frameworks.
/// </remarks>
public interface IMockSupportAttribute {
    /// <summary>
    /// Provides a mock object instance for the specified type.
    /// </summary>
    /// <param name="type">The type for which a mock instance is to be provided.</param>
    /// <returns>A mock object instance of the specified type.</returns>
    object ProvideMock(Type type);

    /// <summary>
    /// Whether this attribute registers <paramref name="serviceType"/> itself for this test, so a
    /// <c>[Mock]</c> parameter naming it should stand aside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a library that separates the double from the object it produces needs this. Moq does:
    /// a <c>Mock&lt;T&gt;</c> parameter makes it register both halves, and they have to be halves of
    /// one mock. A <c>[Mock] T</c> parameter on the same test would otherwise register a second,
    /// unrelated double over the top, and the test would configure one mock while the container
    /// handed out another.
    /// </para>
    /// <para>
    /// False by default, which is right for a library with only one spelling — NSubstitute and
    /// FakeItEasy register nothing of their own, so there is nothing for <c>[Mock]</c> to defer to.
    /// </para>
    /// </remarks>
    /// <param name="testMethod">The test the container is being built for.</param>
    /// <param name="serviceType">The service a <c>[Mock]</c> parameter is about to register.</param>
    bool RegistersService(ITestMethodContext testMethod, Type serviceType) => false;
}
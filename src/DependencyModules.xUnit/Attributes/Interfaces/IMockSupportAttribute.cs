namespace DependencyModules.xUnit.Attributes.Interfaces;

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
}
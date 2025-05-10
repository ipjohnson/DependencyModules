using DependencyModules.xUnit.Attributes.Interfaces;
using NSubstitute;
using NSub = NSubstitute;

namespace DependencyModules.xUnit.NSubstitute;

/// <summary>
/// An attribute that enables support for creating mock objects using NSubstitute
/// in xUnit test contexts.
/// </summary>
/// <remarks>
/// The <c>NSubstituteSupportAttribute</c> provides integration with the NSubstitute
/// library for generating mock instances. This attribute can be applied to classes,
/// methods, or assemblies to enable mock creation for dependency injection during
/// testing scenarios. It implements the <c>IMockSupportAttribute</c> interface
/// to provide mock instances of specified types.
/// </remarks>
/// <example>
/// This attribute is typically used in conjunction with other testing utilities
/// to inject mocked dependencies into test methods or classes.
/// </example>
/// <seealso cref="Substitute"/>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class NSubstituteSupportAttribute : Attribute, IMockSupportAttribute {

    /// <summary>
    /// Provides a mock instance of the specified type using NSubstitute.
    /// </summary>
    /// <param name="type">The type for which a mock instance is to be created.</param>
    /// <returns>A mock object of the specified type.</returns>
    public object ProvideMock(Type type) {
        return NSub.Substitute.For([type], []);
    }
}
using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// The xUnit view of a test method, for hooks that need more than the neutral contract carries.
/// </summary>
/// <remarks>
/// The hooks in <c>DependencyModules.Testing</c> are handed an <see cref="ITestMethodContext"/> so a
/// mocking package can implement them without referencing a test framework at all. An attribute that
/// is already xUnit-specific gives up nothing for that: the context it receives implements this, so
/// <c>if (testMethod is IXunitTestMethodContext xunit)</c> reaches the full model — unique ID, merged
/// traits, generic resolution, the test class and its collection.
/// </remarks>
public interface IXunitTestMethodContext : ITestMethodContext {

    /// <summary>
    /// xUnit's own model of the test method.
    /// </summary>
    IXunitTestMethod XunitTestMethod {
        get;
    }
}

/// <summary>
/// Adapts <see cref="IXunitTestMethod"/> to the neutral contract.
/// </summary>
/// <remarks>
/// The attributes are passed in rather than walked here because the test case has already collected
/// and ordered them to decide which modules to load, and that walk reaches the assembly and the
/// declaring type as well as the method.
/// </remarks>
internal sealed class XunitTestMethodContext(
    IXunitTestMethod testMethod,
    IReadOnlyList<Attribute> attributes) : IXunitTestMethodContext {

    public IXunitTestMethod XunitTestMethod {
        get;
    } = testMethod;

    public MethodInfo Method => XunitTestMethod.Method;

    public IReadOnlyList<Attribute> Attributes {
        get;
    } = attributes;
}

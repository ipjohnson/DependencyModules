using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using NUnit.Framework.Internal;

namespace DependencyModules.NUnit.Impl;

/// <summary>
/// The NUnit view of a test method, for hooks that need more than the neutral contract carries.
/// </summary>
/// <remarks>
/// The hooks in <c>DependencyModules.Testing</c> are handed an <see cref="ITestMethodContext"/> so a
/// mocking package can implement them without referencing a test framework at all. An attribute that
/// is already NUnit-specific gives up nothing for that: the context it receives implements this, so
/// <c>if (testMethod is INUnitTestMethodContext nunit)</c> reaches NUnit's own model — the test's
/// name and id, its properties, and the fixture it belongs to.
/// </remarks>
public interface INUnitTestMethodContext : ITestMethodContext {

    /// <summary>
    /// NUnit's own model of the test method being executed.
    /// </summary>
    TestMethod NUnitTestMethod {
        get;
    }
}

/// <summary>
/// Adapts <see cref="TestMethod"/> to the neutral contract.
/// </summary>
/// <remarks>
/// The attributes are passed in rather than walked here because the command has already collected
/// and ordered them to decide which modules to load, and that walk reaches the assembly and the
/// declaring type as well as the method.
/// </remarks>
internal sealed class NUnitTestMethodContext(
    TestMethod testMethod,
    IReadOnlyList<Attribute> attributes) : INUnitTestMethodContext {

    public TestMethod NUnitTestMethod {
        get;
    } = testMethod;

    public MethodInfo Method => NUnitTestMethod.Method!.MethodInfo;

    public IReadOnlyList<Attribute> Attributes {
        get;
    } = attributes;
}

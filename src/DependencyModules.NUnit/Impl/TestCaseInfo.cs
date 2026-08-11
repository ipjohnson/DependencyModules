using NUnit.Framework.Internal;

namespace DependencyModules.NUnit.Impl;

/// <summary>
/// Defines the contract for retrieving information about a specific test case.
/// </summary>
/// <remarks>
/// Registered in every test's container, so a service can be told what it is being built for.
/// </remarks>
public interface ITestCaseInfo {

    /// <summary>
    /// NUnit's model of the test method being executed, including the arguments the case was
    /// built with, its name and its properties.
    /// </summary>
    TestMethod TestMethod {
        get;
    }

    /// <summary>
    /// Gets the arguments passed to the test method for a specific test case.
    /// </summary>
    /// <remarks>
    /// Set once the container exists and the arguments have been resolved, which is after the
    /// registration itself is made — a service reading this in its constructor would be reading it
    /// too early. Read it from a method the test calls, not from a constructor.
    /// </remarks>
    IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    }

    /// <summary>
    /// Gets the collection of attributes associated with the test method of a specific test case,
    /// widest scope first: assembly, then declaring type, then the method.
    /// </summary>
    IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    }
}

/// <summary>
/// Represents information about a specific test case, including the test method, its arguments, and attributes.
/// </summary>
public class TestCaseInfo(
    TestMethod testMethod,
    IReadOnlyList<object?> testMethodArguments,
    IReadOnlyList<Attribute> testMethodAttributes) : ITestCaseInfo {

    /// <inheritdoc />
    public TestMethod TestMethod {
        get;
    } = testMethod;

    /// <inheritdoc />
    public IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    } = testMethodArguments;

    /// <inheritdoc />
    public IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    } = testMethodAttributes;
}

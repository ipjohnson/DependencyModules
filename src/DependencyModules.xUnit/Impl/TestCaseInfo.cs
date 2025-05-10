using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Defines the contract for retrieving information about a specific test case.
/// </summary>
public interface ITestCaseInfo {

    /// <summary>
    /// Gets the test method associated with a specific test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethod</c> property provides access to the method
    /// that is tied to the test case. This property is particularly
    /// useful when retrieving metadata or executing logic related to
    /// the underlying test method in the context of xUnit.net testing framework.
    /// </remarks>
    IXunitTestMethod TestMethod {
        get;
    }

    /// <summary>
    /// Gets or sets the arguments passed to the test method for a specific test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethodArguments</c> property provides access to the parameters
    /// supplied to the associated test method during its execution. This property
    /// is particularly useful in scenarios where the arguments need to be examined
    /// or manipulated, such as parameterized test cases within the xUnit.net testing framework.
    /// </remarks>
    IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    }

    /// <summary>
    /// Gets the collection of attributes associated with the test method of a specific test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethodAttributes</c> property provides access to metadata attributes
    /// applied to the test method. This property can be utilized to retrieve additional
    /// behavioral or descriptive information tied to the associated test method.
    /// </remarks>
    IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    }
}

/// <summary>
/// Represents information about a specific test case, including the test method, its arguments, and attributes.
/// </summary>
public class TestCaseInfo(
    IXunitTestMethod testMethod,
    IReadOnlyList<object> testMethodArguments,
    IReadOnlyList<Attribute> testMethodAttributes) : ITestCaseInfo {

    /// <summary>
    /// Gets the test method associated with the test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethod</c> property provides access to the underlying test method for a given test case.
    /// It can be utilized to retrieve metadata or invoke specific logic related to the corresponding xUnit test method.
    /// </remarks>
    public IXunitTestMethod TestMethod {
        get;
    } = testMethod;

    /// <summary>
    /// Gets or sets the arguments used for invoking the test method associated with the test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethodArguments</c> property holds the collection of arguments that will be passed to
    /// the test method during execution. This is particularly useful when preparing customized or dynamically
    /// resolved arguments for parameterized test cases.
    /// </remarks>
    public IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    } = testMethodArguments;

    /// <summary>
    /// Gets the collection of attributes applied to the test method associated with a test case.
    /// </summary>
    /// <remarks>
    /// The <c>TestMethodAttributes</c> property provides access to all attributes defined on the test method,
    /// which can be used for customization or additional metadata in the context of the xUnit testing framework.
    /// This property is particularly helpful when injecting behaviors or inspecting the attributes for
    /// parameterized or decorated test methods.
    /// </remarks>
    public IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    } = testMethodAttributes;
}
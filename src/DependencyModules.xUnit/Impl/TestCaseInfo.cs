using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

public interface ITestCaseInfo {
    
    IXunitTestMethod TestMethod {
        get;
    } 

    IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    }

     IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    }
}

public class TestCaseInfo(
    IXunitTestMethod testMethod,
    IReadOnlyList<object> testMethodArguments,
    IReadOnlyList<Attribute> testMethodAttributes) : ITestCaseInfo {
    
    public IXunitTestMethod TestMethod {
        get;
    } = testMethod;

    public IReadOnlyList<object?> TestMethodArguments {
        get;
        set;
    } = testMethodArguments;

    public IReadOnlyList<Attribute> TestMethodAttributes {
        get;
    } = testMethodAttributes;
}
using DependencyModules.xUnit.Attributes;
using SutProject;
using SutProject.Tests.TestFramework;
using Xunit;

[assembly: AssemblyLevelModule]
[assembly: TestRealmModule]

namespace SutProject.Tests.TestFramework;

public class AssemblyTestCaseTests {
    [ModuleTest]
    public void AssemblyTest(ITestRealmService service) {
        Assert.IsType<AssemblyTestCaseService>(service);
    }
}

[ClassLevelModule]
public class ClassTestCaseTests {
    [ModuleTest]
    public void ClassTest(ITestRealmService service) {
        Assert.IsType<ClassTestCaseService>(service);
    }
}

[ClassLevelModule]
public class MethodTestCaseTests {
    [ModuleTest]
    [MethodLevelModule]
    public void MethodTest(ITestRealmService service) {
        Assert.IsType<MethodTestCaseService>(service);
    }
}
using DependencyModules.xUnit.Attributes;
using SutProject;
using SutProject.Tests.TestFramework;
using Xunit;

[assembly: AssemblyLevelModule.Attribute]
[assembly: TestRealmModule.Attribute]

namespace SutProject.Tests.TestFramework;

public class AssemblyTestCaseTests {
    [ModuleTest]
    public void AssemblyTest(ITestRealmService service) {
        Assert.IsType<AssemblyTestCaseService>(service);
    }
}

[ClassLevelModule.Attribute]
public class ClassTestCaseTests {
    [ModuleTest]
    public void ClassTest(ITestRealmService service) {
        Assert.IsType<ClassTestCaseService>(service);
    }
}

[ClassLevelModule.Attribute]
public class MethodTestCaseTests {
    [ModuleTest]
    [MethodLevelModule.Attribute]
    public void MethodTest(ITestRealmService service) {
        Assert.IsType<MethodTestCaseService>(service);
    }
}
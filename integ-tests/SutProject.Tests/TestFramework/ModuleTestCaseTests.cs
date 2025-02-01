using DependencyModules.Testing.Attributes;
using SutProject;
using SutProject.Tests.TestFramework;
using Xunit;

[assembly: AssemblyLevelModule.Module]
[assembly: TestRealmModule.Module]

namespace SutProject.Tests.TestFramework;

public class AssemblyTestCaseTests {
    [ModuleTest]
    public void AssemblyTest(ITestRealmService service) {
        Assert.IsType<AssemblyTestCaseService>(service);
    }
}

[ClassLevelModule.Module]
public class ClassTestCaseTests {
    [ModuleTest]
    public void ClassTest(ITestRealmService service) {
        Assert.IsType<ClassTestCaseService>(service);
    }
}

[ClassLevelModule.Module]
public class MethodTestCaseTests {
    [ModuleTest]
    [MethodLevelModule.Module]
    public void MethodTest(ITestRealmService service) {
        Assert.IsType<MethodTestCaseService>(service);
    }
}
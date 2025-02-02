using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.Customization;

public class CustomDependencyTestCase {
    [ModuleTest]
    [CustomServiceProvider]
    public void TestCase(ICustomTestDependency dependency) {
        //Assert.NotNull(dependency);
    }
}
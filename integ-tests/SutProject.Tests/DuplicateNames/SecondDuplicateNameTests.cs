using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DuplicateNames;

/// <summary>
/// See <see cref="FirstDuplicateNameTests"/>.
/// </summary>
public class SecondDuplicateNameTests {
    [ModuleTest]
    [SutModule]
    public void SharedMethodName(IDependencyOne dependency) {
        Assert.NotNull(dependency);
    }
}

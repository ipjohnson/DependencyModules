using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DuplicateNames;

/// <summary>
/// Paired with <see cref="SecondDuplicateNameTests"/>: both declare a [ModuleTest] method with the
/// same name. A test case unique ID built from the bare method name collides across classes, and
/// xUnit silently drops the duplicate, so both of these must still run.
/// </summary>
public class FirstDuplicateNameTests {
    [ModuleTest]
    [SutModule]
    public void SharedMethodName(IDependencyOne dependency) {
        Assert.NotNull(dependency);
    }
}

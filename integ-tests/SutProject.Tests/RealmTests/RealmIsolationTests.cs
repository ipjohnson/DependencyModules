using DependencyModules.xUnit.Attributes;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.RealmTests;

public class RealmIsolationTests {
    [ModuleTest]
    [FirstRealmModule]
    [SecondarySutModule]
    public void OverrideDependencyWithRealm(IDependencyOne dependencyOne) {
        Assert.IsType<RealmDependencyOne>(dependencyOne);
    }
}
using DependencyModules.Testing.Attributes;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.RealmTests;

public class RealmIsolationTests {
    [ModuleTest]
    [FirstRealmModule.Module]
    [SecondarySutModule.Module]
    public void OverrideDependencyWithRealm(IDependencyOne dependencyOne) {
        Assert.IsType<RealmDependencyOne>(dependencyOne);
    }
}
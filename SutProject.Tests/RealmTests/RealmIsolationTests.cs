using DependencyModules.Testing.Attributes;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.RealmTests;

public class RealmIsolationTests {
    [ModuleTest]
    [LoadModules(typeof(FirstRealmModule), typeof(SecondarySutModule))]
    public void OverrideDependencyWithRealm(IDependencyOne dependencyOne) {
        Assert.IsType<RealmDependencyOne>(dependencyOne);
    }
}
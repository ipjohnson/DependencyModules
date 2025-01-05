using DependencyModules.Testing.Attributes;
using Xunit;

namespace SutProject.Tests.GenericTests;

public class ResolveGenericTypeTests {
    [ModuleTest]
    [LoadModules(typeof(SutModule))]
    public void ResolveGenericType(IGenericInterface<IDependencyOne> genericInterface) {
        Assert.NotNull(genericInterface);
        Assert.NotNull(genericInterface.Value);
    }
}
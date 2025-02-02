using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.GenericTests;


[SutModule.Module]
public class ResolveGenericTypeTests {
    [ModuleTest]
    public void ResolveGenericType(IGenericInterface<IDependencyOne> genericInterface) {
        Assert.NotNull(genericInterface);
        Assert.NotNull(genericInterface.Value);
    }

    [ModuleTest]
    public void ResolveClosedGeneric(IGenericInterface<string> genericInterface) {
        Assert.NotNull(genericInterface);
        Assert.IsType<StringGeneric>(genericInterface);
    }
}
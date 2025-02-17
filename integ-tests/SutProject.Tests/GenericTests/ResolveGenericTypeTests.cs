using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.GenericTests;


[DependencyModule]
public partial class GenericListModule : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddTransient(typeof(IReadOnlyList<>), typeof(List<>));
    }
}

[SutModule.Attribute]
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

    [ModuleTest]
    [SutModule.Attribute]
    [GenericListModule.Attribute]
    public void ResolveList(IReadOnlyList<IDependencyOne> genericList) {
        Assert.NotNull(genericList);
        
    }
}
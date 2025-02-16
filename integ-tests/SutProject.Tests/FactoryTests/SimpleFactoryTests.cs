using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.FactoryTests;

[DependencyModule(OnlyRealm = true)]
public partial class FactoryModule {

}

public static class FactoryClass {

    [SingletonService(Realm = typeof(FactoryModule))]
    public static IDependencyOne FactoryService(
        ISingletonService singletonService, IScopedService scopedService) {
        return new DependencyOne(singletonService, scopedService);
    }

    [SingletonService(Realm = typeof(FactoryModule))]
    public static ISingletonService SingletonService(IServiceProvider serviceProvider) {
        return new SingletonService();
    }

    [ScopedService(Realm = typeof(FactoryModule))]
    public static IScopedService ScopedService() {
        return new ScopedService();
    }
}

public class SimpleFactoryTests {
    [ModuleTest]
    [FactoryModule.Attribute]
    public void FactoryTest(IDependencyOne dependencyOne) {
        Assert.NotNull(dependencyOne);
    }
}
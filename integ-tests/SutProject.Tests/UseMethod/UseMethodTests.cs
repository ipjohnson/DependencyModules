using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.UseMethod;

[DependencyModule(GenerateUseMethod = "UseMethodModule", OnlyRealm = true)]
public partial class UseMethodModule(string name) {
    public string Name => name;
}

[SingletonService(Realm = typeof(UseMethodModule))]
public class SomeImplementation;

public class UseMethodTests {
    [Fact]
    public void UseMethodTest() {
        var serviceCollection = new ServiceCollection();
        serviceCollection.UseMethodModule("testMethod");
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        
        var instance = serviceProvider.GetService<SomeImplementation>();
        Assert.NotNull(instance);
    }
}
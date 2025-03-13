using DependencyModules.Runtime;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class ModuleDuplicationTests {
    [ModuleTest]
    [CombinedModule]
    public void CombinedModuleTest(IEnumerable<IDependencyOne> dependencies) {
        Assert.Single(dependencies);
    }
    
    [ModuleTest]
    [DuplicateModule]
    public void DuplicateModuleTest(IEnumerable<IDependencyOne> dependencies) {
        Assert.Single(dependencies);
    }

    [ModuleTest]
    [CombinedModule]
    [DuplicateModule]
    public void CombinedAndDuplicateModuleTest(IEnumerable<IDependencyOne> dependencies) {
        Assert.Single(dependencies);
    }

    [Fact]
    public void CombinedModuleAddModules() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules(new CombinedModule());
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        
        var dependencies = serviceProvider.GetServices<IDependencyOne>();
        
        Assert.Single(dependencies);
    }

    [Fact]
    public void MultipleDuplicatesAddModules() {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddModules(new CombinedModule(), new DuplicateModule());
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        
        var dependencies = serviceProvider.GetServices<IDependencyOne>();
        
        Assert.Single(dependencies);
    }
}
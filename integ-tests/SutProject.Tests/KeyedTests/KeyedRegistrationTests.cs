using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.KeyedTests;

public class KeyedRegistrationTests {
    [ModuleTest]
    [KeyedModule]
    public void AKeyTest(IServiceProvider serviceProvider) {
        var aService = serviceProvider.GetKeyedService<IKeyedRegistration>("A");
        
        Assert.NotNull(aService);
        Assert.Equal("A", aService.Key);
    }
    
    [ModuleTest]
    [KeyedModule]
    public void BKeyTest(IServiceProvider serviceProvider) {
        var aService = serviceProvider.GetKeyedService<IKeyedRegistration>("B");
        
        Assert.NotNull(aService);
        Assert.Equal("B", aService.Key);
    }
    
    [ModuleTest]
    [KeyedModule]
    public void CKeyTest(IServiceProvider serviceProvider) {
        var aService = serviceProvider.GetKeyedService<IKeyedRegistration>("C");
        
        Assert.NotNull(aService);
        Assert.Equal("C", aService.Key);
    }
    
    [ModuleTest]
    [KeyedModule]
    public void AllKeyTest(
        [FromKeyedServices("A")] IKeyedRegistration aService, 
        [FromKeyedServices("B")] IKeyedRegistration bService, 
        [FromKeyedServices("C")] IKeyedRegistration cService) {
        Assert.NotNull(aService);
        Assert.NotNull(bService);
        Assert.NotNull(cService);
        Assert.Equal("A", aService.Key);
        Assert.Equal("B", bService.Key);
        Assert.Equal("C", cService.Key);
    }
}
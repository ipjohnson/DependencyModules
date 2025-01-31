using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace SutProject.Tests.Customization;

public class CustomServiceProviderAttribute : Attribute, IServiceProviderBuilderAttribute {
    
    public IServiceProvider BuildServiceProvider(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<ICustomTestDependency, CustomTestDependency>();
        return serviceCollection.BuildServiceProvider();
    }
}
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SutProject.Tests.Customization;

public class CustomServiceProviderAttribute : Attribute, IServiceProviderBuilderAttribute {

    public IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<ICustomTestDependency, CustomTestDependency>();
        return serviceCollection.BuildServiceProvider();
    }
}

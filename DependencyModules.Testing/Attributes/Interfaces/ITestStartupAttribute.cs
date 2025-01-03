using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.Testing.Attributes.Interfaces;

public interface ITestStartupAttribute {

    void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection);
    
    Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider);
}
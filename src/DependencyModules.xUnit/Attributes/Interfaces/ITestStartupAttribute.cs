using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes.Interfaces;

public interface ITestStartupAttribute {

    void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection);

    Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider);
}
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes.Interfaces;

public interface IServiceProviderBuilderAttribute {
    IServiceProvider BuildServiceProvider(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection);
}
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.Testing.Attributes.Interfaces;

public interface ITestParameterValueProvider {
    void SetupServiceCollection(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection, ParameterInfo parameter);

    Task<object?> GetParameterValueAsync(IXunitTestMethod context, IServiceProvider serviceProvider, ParameterInfo parameter);
}
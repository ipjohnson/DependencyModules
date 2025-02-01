using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.Testing.Attributes;

/// <summary>
///     Mock service and pass it as parameter to method
/// </summary>
[AttributeUsage(
    AttributeTargets.Parameter,
    AllowMultiple = true)]
public class MockAttribute : Attribute, ITestParameterValueProvider {

    public void SetupServiceCollection(IXunitTestMethod testCaseContext, IServiceCollection serviceCollection, ParameterInfo parameter) {
        var mockAttribute = testCaseContext.Method.GetTestAttribute<IMockSupportAttribute>();

        if (mockAttribute == null) {
            throw new Exception("Mock library not found, please ensure the Type or Assembly is attributed correctly.");
        }

        var mockedValue = mockAttribute.ProvideMock(parameter.ParameterType);

        serviceCollection.AddSingleton(parameter.ParameterType, _ => mockedValue);
    }

    public Task<object?> GetParameterValueAsync(IXunitTestMethod context, IServiceProvider serviceProvider, ParameterInfo parameter) {
        return Task.FromResult(serviceProvider.GetService(parameter.ParameterType));
    }
}
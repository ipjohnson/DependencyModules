using DependencyModules.xUnit.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes;

/// <summary>
///     Export type for test purposes
/// </summary>
[AttributeUsage(
    AttributeTargets.Assembly |
    AttributeTargets.Class |
    AttributeTargets.Method,
    AllowMultiple = true)]
public class TestExportAttribute : Attribute, ITestStartupAttribute {
    public TestExportAttribute(Type service) {
        Service = service;
    }

    public Type Service {
        get;
    }

    public Type? Implementation {
        get;
        set;
    }

    public ServiceLifetime Lifetime {
        get;
        set;
    } = ServiceLifetime.Transient;


    public void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection) {
        var implementation = Implementation ?? Service;

        switch (Lifetime) {
            case ServiceLifetime.Singleton:
                serviceCollection.AddSingleton(Service, implementation);
                break;
            case ServiceLifetime.Scoped:
                serviceCollection.AddScoped(Service, implementation);
                break;
            case ServiceLifetime.Transient:
                serviceCollection.AddTransient(Service, implementation);
                break;
        }
    }

    public Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider) {
        return Task.CompletedTask;
    }
}
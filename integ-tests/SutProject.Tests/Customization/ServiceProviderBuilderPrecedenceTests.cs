using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.Customization;

/// <summary>
/// Records which <see cref="IServiceProviderBuilderAttribute"/> actually built the container.
/// </summary>
public interface IProviderBuiltBy {
    string Scope { get; }
}

public class ProviderBuiltBy(string scope) : IProviderBuiltBy {
    public string Scope => scope;
}

/// <summary>
/// A builder that stamps the container with the scope it was declared at.
/// </summary>
public class ScopeStampingProviderAttribute(string scope) : Attribute, IServiceProviderBuilderAttribute {
    public string Scope => scope;

    public IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IProviderBuiltBy>(new ProviderBuiltBy(scope));

        return serviceCollection.BuildServiceProvider();
    }
}

/// <summary>
/// Only one builder is used, so which one has to be pinned. The narrowest declaration wins, matching
/// how every other test attribute resolves — a method that asks for a particular container is not
/// overridden by a broader default.
/// </summary>
[ScopeStampingProvider("class")]
public class ServiceProviderBuilderPrecedenceTests {

    [ModuleTest]
    [ScopeStampingProvider("method")]
    public void MethodBeatsClass(IProviderBuiltBy builtBy) {
        Assert.Equal("method", builtBy.Scope);
    }

    [ModuleTest]
    public void ClassAppliesWhenTheMethodDeclaresNone(IProviderBuiltBy builtBy) {
        Assert.Equal("class", builtBy.Scope);
    }
}

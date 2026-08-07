using DependencyModules.Runtime;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.StandardTests;

/// <summary>
/// Verifies the lifetimes the generator registers, against a real container built from real
/// generated code.
///
/// Resolving twice from one provider is not enough to tell a singleton from a scoped service:
/// inside a single scope both return the same instance, so an assertion like
/// <c>Assert.Same(a, b)</c> passes either way. Distinguishing them requires crossing a scope
/// boundary, which is what these tests do.
/// </summary>
public class LifetimeSemanticsTests {

    [ModuleTest]
    [SutModule]
    public void Singleton_IsTheSameInstanceAcrossScopes(IServiceProvider provider) {
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var fromFirst = first.ServiceProvider.GetService<ISingletonService>();
        var fromSecond = second.ServiceProvider.GetService<ISingletonService>();

        Assert.NotNull(fromFirst);
        Assert.Same(fromFirst, fromSecond);
    }

    [ModuleTest]
    [SutModule]
    public void Singleton_IsTheSameInstanceAsTheRootProviders(IServiceProvider provider) {
        var fromRoot = provider.GetService<ISingletonService>();

        using var scope = provider.CreateScope();

        Assert.Same(fromRoot, scope.ServiceProvider.GetService<ISingletonService>());
    }

    [ModuleTest]
    [SutModule]
    public void Scoped_IsSharedWithinAScope(IServiceProvider provider) {
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetService<IScopedService>();
        var second = scope.ServiceProvider.GetService<IScopedService>();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    /// <summary>
    /// The assertion that separates scoped from singleton. If the generator registered scoped
    /// services as singletons, this is the test that fails.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void Scoped_DiffersBetweenScopes(IServiceProvider provider) {
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var fromFirst = first.ServiceProvider.GetService<IScopedService>();
        var fromSecond = second.ServiceProvider.GetService<IScopedService>();

        Assert.NotNull(fromFirst);
        Assert.NotNull(fromSecond);
        Assert.NotSame(fromFirst, fromSecond);
    }

    /// <summary>
    /// The assertion that separates transient from both of the others.
    /// </summary>
    [ModuleTest]
    [SutModule]
    public void Transient_IsANewInstanceEveryResolution(IServiceProvider provider) {
        var first = provider.GetService<IDependencyOne>();
        var second = provider.GetService<IDependencyOne>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [ModuleTest]
    [SutModule]
    public void Transient_IsANewInstanceWithinASingleScope(IServiceProvider provider) {
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetService<IDependencyOne>();
        var second = scope.ServiceProvider.GetService<IDependencyOne>();

        Assert.NotSame(first, second);
    }

    [ModuleTest]
    [SutModule]
    public void RegisteredLifetimes_MatchTheirAttributes(IServiceProvider provider) {
        // Resolving proves the wiring; the descriptors prove the lifetime the generator chose.
        var collection = new ServiceCollection();
        collection.AddModule<SutModule>();

        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(collection, d => d.ServiceType == typeof(ISingletonService)).Lifetime);

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(collection, d => d.ServiceType == typeof(IScopedService)).Lifetime);

        Assert.Equal(
            ServiceLifetime.Transient,
            Assert.Single(collection, d => d.ServiceType == typeof(IDependencyOne)).Lifetime);
    }
}

using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.ConventionTests;

/// <summary>
/// The corners of convention registration.
/// </summary>
public class ConventionEdgeTests {

    private static ServiceProvider Provider(params IDependencyModule[] modules) {
        var collection = new ServiceCollection();

        collection.AddModules(modules);

        return collection.BuildServiceProvider();
    }

    /// <summary>
    /// Decoration declared on the module rather than on the class — the form to use when the
    /// decorator or the service comes from an assembly you do not control.
    /// </summary>
    [Fact]
    public void ModuleLevelDecorateWrapsAConventionRegistration() {
        var provider = Provider(new ConventionModuleDecorateModule());

        Assert.Equal("wrapped(core)", provider.GetRequiredService<IModuleDecorated>().Describe());
    }

    /// <summary>
    /// Two modules scanning one interface, composed into one application. Convention registrations
    /// name their declaring module as their realm, so both arrive rather than one displacing the
    /// other.
    /// </summary>
    [Fact]
    public void TwoModulesScanningOneInterfaceBothContribute() {
        var names = Provider(new ConventionSharedFirstModule(), new ConventionSharedSecondModule())
            .GetServices<IShared>()
            .Select(shared => shared.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["first", "second"], names);
    }

    /// <summary>
    /// A namespace filter reaches the namespaces beneath it, or "MyApp.Order" would not cover
    /// "MyApp.Order.Handlers".
    /// </summary>
    [Fact]
    public void InNamespaceOfReachesNestedNamespaces() {
        var names = Provider(new ConventionPrefixNamespaceModule())
            .GetServices<INamespaceScanned>()
            .Select(scanned => scanned.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["nested", "root"], names);
    }

    /// <summary>And InExactNamespaces is how you say you meant only that one.</summary>
    [Fact]
    public void InExactNamespacesExcludesNestedNamespaces() {
        var names = Provider(new ConventionExactNamespaceModule())
            .GetServices<INamespaceScanned>()
            .Select(scanned => scanned.Name)
            .ToArray();

        Assert.Equal(["root"], names);
    }

    [Fact]
    public void NotInNamespaceOfExcludesThatNamespace() {
        var names = Provider(new ConventionExcludedNamespaceModule())
            .GetServices<INamespaceScanned>()
            .Select(scanned => scanned.Name)
            .ToArray();

        Assert.Equal(["root"], names);
    }

    /// <summary>
    /// A generic implementation closing nothing registers as the open generic, and the container
    /// closes it per request.
    /// </summary>
    [Fact]
    public void AnOpenGenericRegistrationResolvesAtEveryClosing() {
        var provider = Provider(new ConventionOpenGenericModule());

        Assert.Equal("cache:String", provider.GetRequiredService<IOpenCache<string>>().Describe());
        Assert.Equal("cache:Int32", provider.GetRequiredService<IOpenCache<int>>().Describe());
    }

    [Fact]
    public void ADeclaredScopedLifetimeActuallyScopes() {
        var provider = Provider(new ConventionLifetimeModule());

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var one = first.ServiceProvider.GetRequiredService<IScopedByConvention>();

        Assert.Same(one, first.ServiceProvider.GetRequiredService<IScopedByConvention>());
        Assert.NotSame(one, second.ServiceProvider.GetRequiredService<IScopedByConvention>());
    }

    /// <summary>
    /// The container owns what it constructed, however the registration was declared.
    /// </summary>
    [Fact]
    public void AConventionRegisteredSingletonIsDisposedWithTheProvider() {
        var provider = Provider(new ConventionLifetimeModule());
        var disposable = provider.GetRequiredService<IDisposableByConvention>();

        Assert.False(disposable.Disposed);

        provider.Dispose();

        Assert.True(disposable.Disposed);
    }

    /// <summary>
    /// An internal implementation is a candidate in the compilation being built. The same type in a
    /// referenced assembly is not, because only public types cross the boundary — a difference
    /// nothing can report, since it cannot see what it cannot see.
    /// </summary>
    [Fact]
    public void AnInternalImplementationIsACandidateInThisCompilation() {
        var provider = Provider(new ConventionLifetimeModule());

        Assert.Equal("internal", provider.GetRequiredService<IInternallyImplemented>().Name);
    }

    /// <summary>
    /// Decoration passes the wrapped instance positionally and resolves everything else from the
    /// container, so a decorator's own dependencies can be convention-registered too.
    /// </summary>
    [Fact]
    public void ADecoratorResolvesItsOwnConventionRegisteredDependencies() {
        var provider = Provider(new ConventionDecoratorDependencyModule());

        Assert.Equal("dep(core)", provider.GetRequiredService<IDependentlyDecorated>().Describe());
    }

    /// <summary>
    /// Filters and shapes apply to a metadata scan the same way they apply to local types.
    /// </summary>
    [Fact]
    public void AFilteredMetadataScanRegistersTheConcreteType() {
        var provider = Provider(new ConventionFilteredScanModule());

        Assert.Equal("first", provider.GetRequiredService<FirstPackagePolicy>().Name);

        // Narrowed by name, and reshaped, so neither the interface nor the other policy is there.
        Assert.Null(provider.GetService<IPackagePolicy>());
        Assert.Null(provider.GetService<SecondPackagePolicy>());
    }
}

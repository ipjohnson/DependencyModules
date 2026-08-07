using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.CrossWire;

[DependencyModule(OnlyRealm = true)]
public partial class CrossWireModule {

}

[DependencyModule(OnlyRealm = true)]
public partial class CrossWireScopedModule {

}

public interface IInterface1 {

}

public interface IInterface2 {

}

[CrossWireService(Realm = typeof(CrossWireModule))]
public class CrossWireService : IInterface1, IInterface2 {

}

[CrossWireService(Lifetime = ServiceLifetime.Scoped, Realm = typeof(CrossWireScopedModule))]
public class ScopedCrossWireService : IInterface1, IInterface2 {

}

/// <summary>
/// The contract of [CrossWireService] is that one instance is reachable through the implementation
/// type and through every interface it implements. Asserting that a resolved service equals itself
/// would pass no matter what the generator emitted, so each test here compares the instances
/// obtained through different service types.
/// </summary>
public class CrossWireTests {

    [ModuleTest]
    [CrossWireModule]
    public void CrossWire_SharesOneInstanceAcrossItsInterfaces(IInterface1 interface1, IInterface2 interface2) {
        Assert.NotNull(interface1);
        Assert.NotNull(interface2);
        Assert.Same(interface1, interface2);
    }

    [ModuleTest]
    [CrossWireModule]
    public void CrossWire_ResolvesTheImplementationType(
        IInterface1 interface1, CrossWireService implementation) {

        Assert.NotNull(implementation);
        Assert.Same(interface1, implementation);
    }

    [ModuleTest]
    [CrossWireModule]
    public void CrossWire_DefaultsToASingleInstanceAcrossScopes(IServiceProvider provider) {
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetService<IInterface1>(),
            second.ServiceProvider.GetService<IInterface1>());
    }

    [ModuleTest]
    [CrossWireScopedModule]
    public void ScopedCrossWire_SharesOneInstanceWithinAScope(IServiceProvider provider) {
        using var scope = provider.CreateScope();

        var asInterface1 = scope.ServiceProvider.GetService<IInterface1>();
        var asInterface2 = scope.ServiceProvider.GetService<IInterface2>();

        Assert.NotNull(asInterface1);
        Assert.Same(asInterface1, asInterface2);
    }

    /// <summary>
    /// The assertion that makes the scoped variant meaningfully different from the singleton one.
    /// </summary>
    [ModuleTest]
    [CrossWireScopedModule]
    public void ScopedCrossWire_DiffersBetweenScopes(IServiceProvider provider) {
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetService<IInterface1>(),
            second.ServiceProvider.GetService<IInterface1>());
    }
}

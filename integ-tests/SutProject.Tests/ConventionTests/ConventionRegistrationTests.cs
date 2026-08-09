using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.ConventionTests;

/// <summary>
/// Convention registration compiled by the real analyzer through MSBuild, then resolved from a real
/// provider.
/// </summary>
/// <remarks>
/// The generator unit tests drive Roslyn in memory, which cannot show that the analyzer loads from
/// an ordinary ProjectReference, that its post-initialization contract types are visible to the code
/// that implements them, or that two analyzer packages contribute to the same partial module without
/// colliding. That is what this file is for.
/// </remarks>
[ConventionSutModule]
public class ConventionRegistrationTests {

    [ModuleTest]
    public void RegistersTypeDeclaringTheInterfaceDirectly(IEnumerable<IConventionService> services) {
        Assert.Contains(services, service => service.Name == "direct");
    }

    [ModuleTest]
    public void RegistersTypeReachingTheInterfaceThroughInterfaceInheritance(
        IEnumerable<IConventionService> services) {
        Assert.Contains(services, service => service.Name == "inherited");
    }

    [ModuleTest]
    public void RegistersExactlyTheTwoMatchingTypes(IEnumerable<IConventionService> services) {
        Assert.Equal(
            new[] { "direct", "inherited" },
            services.Select(service => service.Name).OrderBy(name => name).ToArray());
    }

    [ModuleTest]
    public void RegistersTheDeclaredLifetime(IConventionService first, IConventionService second) {
        // Declared AsSingleton, so one instance serves both parameters.
        Assert.Same(first, second);
    }

    // -----------------------------------------------------------------------
    // Open generics.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The make-or-break behaviour: an open generic convention registers each match against the
    /// closed construction it actually implements, not against the open definition.
    /// </summary>
    [ModuleTest]
    public void ClosesAnOpenGenericAgainstEachImplementation(
        IConventionHandler<CreateOrder, OrderId> create,
        IConventionHandler<RenameOrder, OrderId> rename) {

        Assert.IsType<CreateOrderHandler>(create);
        Assert.IsType<RenameOrderHandler>(rename);

        Assert.Equal(1, create.Handle(new CreateOrder()).Value);
        Assert.Equal(2, rename.Handle(new RenameOrder()).Value);
    }

    /// <summary>
    /// A generic implementation passing its own parameter straight through registers as the open
    /// generic, so the container closes it per request.
    /// </summary>
    [ModuleTest]
    public void RegistersAGenericImplementationAsAnOpenGeneric(
        IConventionCache<int> ints, IConventionCache<string> strings) {

        Assert.IsType<PassThroughCache<int>>(ints);
        Assert.IsType<PassThroughCache<string>>(strings);

        Assert.Equal("open:Int32", ints.Describe());
        Assert.Equal("open:String", strings.Describe());
    }

    /// <summary>
    /// Reaching an open generic through interface inheritance still registers the closed
    /// construction: StringStore declares IAuditedStore&lt;string&gt;, never IConventionStore&lt;string&gt;.
    /// </summary>
    [ModuleTest]
    public void ClosesAnOpenGenericReachedThroughInterfaceInheritance(IConventionStore<string> store) {
        Assert.IsType<StringStore>(store);
        Assert.Equal("audited:string", store.Describe());
    }

    /// <summary>An open generic convention registers nothing for a construction nobody implements.</summary>
    [ModuleTest]
    public void DoesNotRegisterAnUnimplementedConstruction(IServiceProvider provider) {
        Assert.Null(provider.GetService<IConventionStore<int>>());
        Assert.Null(provider.GetService<IConventionHandler<CreateOrder, CreateOrder>>());
    }

    // -----------------------------------------------------------------------
    // Precedence.
    // -----------------------------------------------------------------------

    [ModuleTest]
    public void AnExplicitAttributeStillRegistersAlongsideTheConvention(
        IEnumerable<IAttributeWinsService> services) {

        var names = services.Select(service => service.Name).OrderBy(name => name).ToArray();

        Assert.Equal(new[] { "attributed", "by-convention" }, names);
    }

    [ModuleTest]
    public void TheAttributedTypeKeepsItsOwnLifetime(
        IEnumerable<IAttributeWinsService> first, IEnumerable<IAttributeWinsService> second) {

        // The attribute declared Singleton and the convention declared Transient. The attributed
        // type is registered once, by its attribute, so it survives across resolutions while the
        // convention-registered one does not.
        var attributedFirst = first.Single(service => service.Name == "attributed");
        var attributedSecond = second.Single(service => service.Name == "attributed");

        Assert.Same(attributedFirst, attributedSecond);

        var conventionFirst = first.Single(service => service.Name == "by-convention");
        var conventionSecond = second.Single(service => service.Name == "by-convention");

        Assert.NotSame(conventionFirst, conventionSecond);
    }
}

/// <summary>
/// The base-class hop, proven from both sides against the same interface.
/// </summary>
public class ConventionBaseClassReachTests {

    [ModuleTest]
    [ConventionNoBaseClassModule]
    public void ABaseClassHopIsNotMatchedByDefault(IEnumerable<IBaseClassReachService> services) {
        Assert.Equal(
            new[] { "direct-reach" },
            services.Select(service => service.Name).OrderBy(name => name).ToArray());
    }

    [ModuleTest]
    [ConventionBaseClassModule]
    public void ABaseClassHopIsMatchedWhenOptedIn(IEnumerable<IBaseClassReachService> services) {
        Assert.Equal(
            new[] { "direct-reach", "through-base" },
            services.Select(service => service.Name).OrderBy(name => name).ToArray());
    }
}

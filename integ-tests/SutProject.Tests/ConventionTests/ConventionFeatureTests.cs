using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using SecondarySutProject;
using Xunit;

namespace SutProject.Tests.ConventionTests;

/// <summary>
/// The selection, shape and decoration features, compiled by the real analyzer through MSBuild.
/// </summary>
/// <remarks>
/// The generator unit tests drive Roslyn in memory. They cannot show that the analyzer loads from an
/// ordinary ProjectReference, that both analyzer packages contribute to one partial module without
/// colliding, or — for <c>InAssemblyOf</c> — that a genuine compile-time assembly reference is what
/// gets scanned. That is what this file is for.
/// </remarks>
public class ConventionFeatureTests {

    private static ServiceProvider Provider(params DependencyModules.Runtime.Interfaces.IDependencyModule[] modules) {
        var collection = new ServiceCollection();

        collection.AddModules(modules);

        return collection.BuildServiceProvider();
    }

    private static IServiceCollection Collection(
        params DependencyModules.Runtime.Interfaces.IDependencyModule[] modules) {

        var collection = new ServiceCollection();

        collection.AddModules(modules);

        return collection;
    }

    [Fact]
    public void AsSelfRegistersTheConcreteType() {
        var provider = Provider(new ConventionAsSelfModule());

        Assert.NotNull(provider.GetService<SelfShaped>());
        Assert.Null(provider.GetService<IShapeService>());
    }

    [Fact]
    public void AsSelfWithInterfacesSharesOneInstanceAndSkipsSystemInterfaces() {
        var provider = Provider(new ConventionCrossWireModule());

        var asConcrete = provider.GetRequiredService<CrossWiredShape>();

        Assert.Same(asConcrete, provider.GetRequiredService<IAlsoShaped>());
        Assert.Same(asConcrete, provider.GetRequiredService<IShapeService>());

        // Reachable, but cross-wiring a BCL interface is never what "as its interfaces" means.
        Assert.Null(provider.GetService<IDisposable>());
    }

    [Fact]
    public void AlsoAsSelfRegistersBothAndSharesOneInstance() {
        var provider = Provider(new ConventionAlsoAsSelfModule());

        var asInterface = provider.GetRequiredService<IAlsoSelfService>();
        var asConcrete = provider.GetRequiredService<AlsoSelfShape>();

        Assert.Same(asInterface, asConcrete);

        // Only the interfaces the convention matched, not everything the type reaches.
        Assert.Null(provider.GetService<IShapeService>());
    }

    [Fact]
    public void WithAttributeSelectsOnlyMarkedTypes() {
        var services = Provider(new ConventionAttributeFilterModule())
            .GetServices<IFiltered>()
            .Select(service => service.Name)
            .ToArray();

        Assert.Equal(["marked"], services);
    }

    [Fact]
    public void WithNameSelectsOnTheGlob() {
        var services = Provider(new ConventionNameFilterModule())
            .GetServices<IFiltered>()
            .Select(service => service.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["marked", "unmarked"], services);
    }

    /// <summary>
    /// A concrete type with no interface, selected by namespace and name alone.
    /// </summary>
    [Fact]
    public void RegisterAllWithFiltersRegistersTypesThatImplementNothing() {
        var provider = Provider(new ConventionNamespaceModule());

        Assert.NotNull(provider.GetService<NamespaceOnlyCalculator>());
        Assert.NotNull(provider.GetService<NamespaceOnlyMarker>());
    }

    [Fact]
    public void AsMatchingInterfaceRegistersRenamerAsIRenamer() {
        var provider = Provider(new ConventionMatchingInterfaceModule());

        Assert.IsType<Renamer>(provider.GetRequiredService<IRenamer>());
        Assert.Null(provider.GetService<INamedRoot>());
    }

    [Fact]
    public void AsRegistersUnderTheNamedServiceType() {
        var provider = Provider(new ConventionAsModule());

        Assert.IsType<ExplicitlyRegistered>(provider.GetRequiredService<IExplicitTarget>());
        Assert.Null(provider.GetService<IExplicitSource>());
    }

    [Fact]
    public void WithKeyRegistersUnderAServiceKey() {
        var provider = Provider(new ConventionKeyModule());

        Assert.IsType<KeyedOne>(provider.GetRequiredKeyedService<IKeyedService>("primary"));
        Assert.Null(provider.GetService<IKeyedService>());
    }

    [Fact]
    public void UsingTryRegistersTheServiceTypeOnce() {
        Assert.Single(
            Collection(new ConventionUsingModule()),
            descriptor => descriptor.ServiceType == typeof(ITriedService));
    }

    /// <summary>
    /// A type filling two roles registers as both, each with its own lifetime.
    /// </summary>
    [Fact]
    public void ATypeMatchedThroughTwoInterfacesRegistersAsBoth() {
        var collection = Collection(new ConventionTwoRolesModule());

        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(collection, d => d.ServiceType == typeof(IFirstRole)).Lifetime);

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(collection, d => d.ServiceType == typeof(ISecondRole)).Lifetime);
    }

    /// <summary>
    /// One convention registers every closing a candidate implements. Registering only the first
    /// left the second silently unregistered.
    /// </summary>
    [Fact]
    public void OneConventionRegistersEveryClosing() {
        var provider = Provider(new ConventionClosingsModule());

        Assert.IsType<OrderEvents>(provider.GetRequiredService<INotification<OrderPlaced>>());
        Assert.IsType<OrderEvents>(provider.GetRequiredService<INotification<OrderShipped>>());
    }

    /// <summary>
    /// One open generic decorator over every handler a convention registered — the MediatR shape.
    /// </summary>
    [Fact]
    public void ADecoratorWrapsEveryConventionRegisteredHandler() {
        var provider = Provider(new ConventionDecoratedHandlerModule());
        var log = provider.GetRequiredService<HandlerLog>();

        var create = provider.GetRequiredService<IRequestHandler<CreateThing, ThingResult>>();
        var rename = provider.GetRequiredService<IRequestHandler<RenameThing, ThingResult>>();

        Assert.IsType<LoggingRequestHandler<CreateThing, ThingResult>>(create);
        Assert.IsType<LoggingRequestHandler<RenameThing, ThingResult>>(rename);

        Assert.Equal("created", create.Handle(new CreateThing()).Value);
        Assert.Equal("renamed", rename.Handle(new RenameThing()).Value);

        Assert.Equal(["handling CreateThing", "handling RenameThing"], log.Lines);
    }

    /// <summary>
    /// The decorator is not itself registered. It implements the interface it decorates, so a
    /// convention scanning that interface would otherwise match it — and being generic and closing
    /// nothing, it would register as the open generic and make the whole module undecoratable.
    /// </summary>
    [Fact]
    public void ADecoratorIsNotRegisteredAsAService() {
        var collection = Collection(new ConventionDecoratedHandlerModule());

        Assert.DoesNotContain(collection, descriptor => descriptor.ServiceType.IsGenericTypeDefinition);

        Assert.Equal(
            2,
            collection.Count(descriptor =>
                descriptor.ServiceType.IsGenericType &&
                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));
    }

    /// <summary>
    /// Scanning a genuinely referenced assembly, where there is no syntax to read.
    /// </summary>
    [Fact]
    public void InAssemblyOfRegistersPublicTypesFromTheReferencedAssembly() {
        var policies = Provider(new ConventionAssemblyScanModule())
            .GetServices<IPackagePolicy>()
            .Select(policy => policy.Name)
            .OrderBy(name => name)
            .ToArray();

        // The internal policy in that assembly is invisible across the boundary.
        Assert.Equal(["first", "second"], policies);
    }
}

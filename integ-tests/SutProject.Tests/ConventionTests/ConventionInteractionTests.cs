using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.ConventionTests;

/// <summary>
/// Conventions crossed with the rest of the library.
/// </summary>
/// <remarks>
/// Each feature works on its own; what nobody had run is the combinations. Interception and
/// decoration do not know a service was registered by convention, conventions do not know a
/// candidate is intercepted, and the type shapes people write are not all plain classes.
/// </remarks>
public class ConventionInteractionTests {

    private static ServiceProvider Provider(
        IModuleEnvironment? environment, params IDependencyModule[] modules) {

        var collection = new ServiceCollection();

        collection.AddModules(environment, modules);

        return collection.BuildServiceProvider();
    }

    private static ServiceProvider Provider(params IDependencyModule[] modules) =>
        Provider(null, modules);

    /// <summary>
    /// A convention-registered service is still intercepted. [Intercept] is not a service
    /// attribute, so the class stays a candidate, and the two generators have to agree about what
    /// ends up registered.
    /// </summary>
    [Fact]
    public void AConventionRegisteredServiceIsIntercepted() {
        var provider = Provider(new ConventionInterceptModule());

        var service = provider.GetRequiredService<IInterceptedByConvention>();
        var log = provider.GetRequiredService<InterceptLog>();

        Assert.Equal("worked", service.Work());
        Assert.Equal(["intercepted Work"], log.Lines);
    }

    /// <summary>
    /// Two decorators nest by declared order, lower closest to the implementation.
    /// </summary>
    [Fact]
    public void DecoratorsNestByOrderOverAConventionRegistration() {
        var provider = Provider(new ConventionOrderedDecoratorModule());

        Assert.Equal("outer(inner(core))", provider.GetRequiredService<IOrdered>().Describe());
    }

    /// <summary>
    /// Decoration rewrites a keyed registration in place, keeping the key.
    /// </summary>
    [Fact]
    public void AKeyedConventionRegistrationIsDecorated() {
        var provider = Provider(new ConventionKeyedDecoratedModule());

        Assert.Equal(
            "wrapped(core)",
            provider.GetRequiredKeyedService<IKeyedAndDecorated>("main").Describe());
    }

    /// <summary>
    /// Records, nested types and primary constructors are all ordinary candidates, and one
    /// convention registration can be injected into another.
    /// </summary>
    [Fact]
    public void RecordsNestedTypesAndPrimaryConstructorsAreCandidates() {
        var names = Provider(new ConventionShapesModule())
            .GetServices<IShaped>()
            .Select(shaped => shaped.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["nested", "primary-dep", "record"], names);
    }

    /// <summary>
    /// An OnlyRealm module takes its own convention registrations, which name it as their realm.
    /// </summary>
    [Fact]
    public void ARealmModuleTakesItsOwnConventionRegistrations() {
        var provider = Provider(new ConventionRealmModule());

        Assert.Equal("realm", provider.GetRequiredService<IRealmScoped>().Name);
    }

    /// <summary>
    /// Composing a module brings its conventions with it, the same as its attribute registrations.
    /// </summary>
    [Fact]
    public void ComposingAModuleBringsItsConventions() {
        var provider = Provider(new ConventionCompositionModule());

        Assert.Equal("composed", provider.GetRequiredService<IComposedService>().Name);
    }

    /// <summary>
    /// A condition on a convention candidate is honoured. The class carries no service attribute,
    /// so the convention is its only route into the container — dropping the condition would put a
    /// development-only service into production.
    /// </summary>
    [Theory]
    [InlineData("Development", new[] { "always", "development" })]
    [InlineData("Production", new[] { "always" })]
    public void EnvironmentConditionsApplyToConventionCandidates(
        string environmentName, string[] expected) {

        var names = Provider(new ModuleEnvironment(environmentName), new ConventionConditionalModule())
            .GetServices<IConditionalByConvention>()
            .Select(service => service.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(expected, names);
    }

    /// <summary>
    /// Nothing here registers into another module's realm. Convention registrations name their
    /// declaring module, so two modules scanning the same interface do not leak into each other.
    /// </summary>
    [Fact]
    public void ConventionRegistrationsDoNotLeakBetweenModules() {
        var onlyShapes = Provider(new ConventionShapesModule());

        Assert.Empty(onlyShapes.GetServices<IOrdered>());
        Assert.Empty(onlyShapes.GetServices<IComposedService>());
    }
}

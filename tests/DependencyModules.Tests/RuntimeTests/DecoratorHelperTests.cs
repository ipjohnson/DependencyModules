using DependencyModules.Runtime.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// The descriptor rewrite is where decoration actually goes wrong, so it is tested directly rather
/// than only through generated code.
/// </summary>
public class DecoratorHelperTests {

    private interface IThing {
        string Describe();
    }

    private class Thing : IThing {
        public string Describe() => "thing";
    }

    private class OtherThing : IThing {
        public string Describe() => "other";
    }

    private class Wrapper(IThing inner) : IThing {
        public IThing Inner { get; } = inner;

        public string Describe() => $"wrapped({Inner.Describe()})";
    }

    private class SecondWrapper(IThing inner) : IThing {
        public string Describe() => $"second({inner.Describe()})";
    }

    private static IThing Resolve(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IThing>();

    [Fact]
    public void Decorate_WrapsAnImplementationTypeRegistration() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.Equal("wrapped(thing)", Resolve(services).Describe());
    }

    [Fact]
    public void Decorate_WrapsAFactoryRegistration() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing>(_ => new Thing());

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.Equal("wrapped(thing)", Resolve(services).Describe());
    }

    [Fact]
    public void Decorate_WrapsAnInstanceRegistration() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing>(new Thing());

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.Equal("wrapped(thing)", Resolve(services).Describe());
    }

    /// <summary>
    /// The failure this guards against is not a wrong answer but a stack overflow: if the factory
    /// reads the collection slot instead of the captured descriptor, it resolves itself forever.
    /// </summary>
    [Fact]
    public void Decorate_DoesNotRecurseIntoItsOwnReplacement() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        var resolved = Assert.IsType<Wrapper>(Resolve(services));
        Assert.IsType<Thing>(resolved.Inner);
    }

    [Fact]
    public void Decorate_WrapsEveryRegistrationOfTheService() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IThing, OtherThing>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        var all = services.BuildServiceProvider().GetServices<IThing>().ToArray();

        Assert.Equal(2, all.Length);
        Assert.All(all, thing => Assert.IsType<Wrapper>(thing));
        Assert.Equal(["wrapped(thing)", "wrapped(other)"], all.Select(t => t.Describe()));
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void Decorate_PreservesTheOriginalLifetime(ServiceLifetime lifetime) {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IThing), typeof(Thing), lifetime));

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.Equal(lifetime, Assert.Single(services).Lifetime);
    }

    [Fact]
    public void Decorate_LeavesOtherServicesAlone() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IUnrelated, Unrelated>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.IsType<Unrelated>(services.BuildServiceProvider().GetRequiredService<IUnrelated>());
    }

    private interface IUnrelated;

    private class Unrelated : IUnrelated;

    [Fact]
    public void Decorate_WithNoMatchingRegistration_DoesNothing() {
        var services = new ServiceCollection();
        services.AddSingleton<IUnrelated, Unrelated>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        Assert.Single(services);
    }

    [Fact]
    public void Decorate_AppliedTwice_NestsInApplicationOrder() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));
        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new SecondWrapper((IThing)inner));

        // Applied first ends up innermost.
        Assert.Equal("second(wrapped(thing))", Resolve(services).Describe());
    }

    [Fact]
    public void Decorate_ResolvesTheDecoratorsOwnDependencies() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IUnrelated, Unrelated>();

        DecoratorHelper.Decorate(services, typeof(IThing),
            (provider, inner) => new DependentWrapper((IThing)inner, provider.GetRequiredService<IUnrelated>()));

        var resolved = Assert.IsType<DependentWrapper>(Resolve(services));
        Assert.NotNull(resolved.Dependency);
    }

    private class DependentWrapper(IThing inner, IUnrelated dependency) : IThing {
        public IUnrelated Dependency { get; } = dependency;

        public string Describe() => inner.Describe();
    }

    private interface IGeneric<T> {
        string Describe();
    }

    private class GenericThing<T> : IGeneric<T> {
        public string Describe() => $"generic<{typeof(T).Name}>";
    }

    private class GenericWrapper<T>(IGeneric<T> inner) : IGeneric<T> {
        public string Describe() => $"wrapped({inner.Describe()})";
    }

    /// <summary>
    /// Decorating an open generic has to wrap each closed registration of it. This is the shape a
    /// mediator pipeline behaviour takes.
    /// </summary>
    [Fact]
    public void Decorate_OpenGeneric_WrapsEveryClosedRegistration() {
        var services = new ServiceCollection();
        services.AddSingleton<IGeneric<string>, GenericThing<string>>();
        services.AddSingleton<IGeneric<int>, GenericThing<int>>();

        DecoratorHelper.Decorate(services, typeof(IGeneric<>), (_, inner) => {
            var argument = inner.GetType().GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IGeneric<>))
                .GetGenericArguments()[0];

            return Activator.CreateInstance(typeof(GenericWrapper<>).MakeGenericType(argument), inner)!;
        });

        var provider = services.BuildServiceProvider();

        Assert.Equal("wrapped(generic<String>)", provider.GetRequiredService<IGeneric<string>>().Describe());
        Assert.Equal("wrapped(generic<Int32>)", provider.GetRequiredService<IGeneric<int>>().Describe());
    }

    [Fact]
    public void Decorate_OpenGeneric_LeavesUnrelatedClosedTypesAlone() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IGeneric<string>, GenericThing<string>>();

        DecoratorHelper.Decorate(services, typeof(IGeneric<>), (_, inner) => inner);

        Assert.IsType<Thing>(Resolve(services));
    }

    [Fact]
    public void Decorate_KeyedRegistration_IsWrappedAndKeepsItsKey() {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IThing, Thing>("the-key");

        DecoratorHelper.Decorate(services, typeof(IThing), (_, inner) => new Wrapper((IThing)inner));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IThing>("the-key");

        Assert.Equal("wrapped(thing)", resolved.Describe());
    }

    // --- the type-based overload generated code calls ---

    [Fact]
    public void DecorateByType_WrapsAndResolvesTheDecoratorsDependencies() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IUnrelated, Unrelated>();

        DecoratorHelper.Decorate(services, typeof(IThing), typeof(DependentWrapper));

        var resolved = Assert.IsType<DependentWrapper>(Resolve(services));
        Assert.NotNull(resolved.Dependency);
    }

    [Fact]
    public void DecorateByType_PassesTheWrappedInstance() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(services, typeof(IThing), typeof(Wrapper));

        Assert.Equal("wrapped(thing)", Resolve(services).Describe());
    }

    [Fact]
    public void DecorateByType_OpenGeneric_ClosesTheDecoratorPerRegistration() {
        var services = new ServiceCollection();
        services.AddSingleton<IGeneric<string>, GenericThing<string>>();
        services.AddSingleton<IGeneric<int>, GenericThing<int>>();

        DecoratorHelper.Decorate(services, typeof(IGeneric<>), typeof(GenericWrapper<>));

        var provider = services.BuildServiceProvider();

        Assert.Equal("wrapped(generic<String>)", provider.GetRequiredService<IGeneric<string>>().Describe());
        Assert.Equal("wrapped(generic<Int32>)", provider.GetRequiredService<IGeneric<int>>().Describe());
    }

    /// <summary>
    /// Stacking open generic decorators means the wrapped instance is itself a decorator, so the
    /// type arguments have to be discovered from whichever of them implements the service.
    /// </summary>
    [Fact]
    public void DecorateByType_OpenGeneric_Stacks() {
        var services = new ServiceCollection();
        services.AddSingleton<IGeneric<string>, GenericThing<string>>();

        DecoratorHelper.Decorate(services, typeof(IGeneric<>), typeof(GenericWrapper<>));
        DecoratorHelper.Decorate(services, typeof(IGeneric<>), typeof(GenericWrapper<>));

        var provider = services.BuildServiceProvider();

        Assert.Equal("wrapped(wrapped(generic<String>))", provider.GetRequiredService<IGeneric<string>>().Describe());
    }

    [Fact]
    public void DecorateByType_StacksInApplicationOrder() {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(services, typeof(IThing), typeof(Wrapper));
        DecoratorHelper.Decorate(services, typeof(IThing), typeof(SecondWrapper));

        Assert.Equal("second(wrapped(thing))", Resolve(services).Describe());
    }
}

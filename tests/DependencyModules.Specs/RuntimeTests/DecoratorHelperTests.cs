using DependencyModules.Runtime.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

/// <summary>
/// The descriptor rewrite is where decoration actually goes wrong, so it is tested directly rather
/// than only through generated code.
/// </summary>
public partial class DecoratorHelperTests
{
    private interface IThing
    {
        string Describe();
    }

    private class Thing : IThing
    {
        public string Describe() => "thing";
    }

    private class OtherThing : IThing
    {
        public string Describe() => "other";
    }

    private class Wrapper(IThing inner) : IThing
    {
        public IThing Inner { get; } = inner;

        public string Describe() => $"wrapped({Inner.Describe()})";
    }

    private class SecondWrapper(IThing inner) : IThing
    {
        public string Describe() => $"second({inner.Describe()})";
    }

    private interface IRepo<T>
    {
        string Describe();
    }

    private class Repo<T> : IRepo<T>
    {
        public string Describe() => "repo";
    }

    private class RepoWrapper<T>(IRepo<T> inner) : IRepo<T>
    {
        public string Describe() => $"wrapped({inner.Describe()})";
    }

    private class StringRepo : Repo<string>;

    private class DisposableThing : IThing, IDisposable
    {
        public int Disposals { get; private set; }

        public string Describe() => "thing";

        public void Dispose() => Disposals++;
    }

    private class DisposableWrapper(IThing inner) : IThing, IDisposable
    {
        public IThing Inner { get; } = inner;

        public int Disposals { get; private set; }

        public string Describe() => $"wrapped({Inner.Describe()})";

        public void Dispose() => Disposals++;
    }

    private static IThing Resolve(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IThing>();

    [VerifyTest]
    public void Decorate_WrapsAnImplementationTypeRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate(
            services,
            typeof(IThing),
            (_, inner) => new Wrapper((IThing)inner)
        );

        Verify(Resolve(services).Describe() == "wrapped(thing)");
    }

    [VerifyTest]
    public void Decorate_WrapsAFactoryRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing>(_ => new Thing());

        DecoratorHelper.Decorate(
            services,
            typeof(IThing),
            (_, inner) => new Wrapper((IThing)inner)
        );

        Verify(Resolve(services).Describe() == "wrapped(thing)");
    }

    [VerifyTest]
    public void Decorate_WrapsAnInstanceRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing>(new Thing());

        DecoratorHelper.Decorate(
            services,
            typeof(IThing),
            (_, inner) => new Wrapper((IThing)inner)
        );

        Verify(Resolve(services).Describe() == "wrapped(thing)");
    }

    /// <summary>
    /// The generic overload wraps the same shapes the type-driven one does, without a cast at the
    /// call site.
    /// </summary>
    [VerifyTest]
    public void DecorateOfT_WrapsAnImplementationTypeRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate<IThing>(
            services,
            typeof(Wrapper),
            (_, inner) => new Wrapper(inner)
        );

        Verify(Resolve(services).Describe() == "wrapped(thing)");
    }

    [VerifyTest]
    public void DecorateOfT_WrapsEveryRegistrationOfTheService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IThing, OtherThing>();

        DecoratorHelper.Decorate<IThing>(
            services,
            typeof(Wrapper),
            (_, inner) => new Wrapper(inner)
        );

        var all = services.BuildServiceProvider().GetServices<IThing>().ToArray();

        Verify(all.All(thing => thing.GetType() == typeof(Wrapper)));
        Verify(all.Select(t => t.Describe()).SequenceEqual(["wrapped(thing)", "wrapped(other)"]));
    }

    [VerifyTest]
    public void DecorateOfT_StacksInApplicationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, Thing>();

        DecoratorHelper.Decorate<IThing>(
            services,
            typeof(Wrapper),
            (_, inner) => new Wrapper(inner)
        );
        DecoratorHelper.Decorate<IThing>(
            services,
            typeof(SecondWrapper),
            (_, inner) => new SecondWrapper(inner)
        );

        Verify(Resolve(services).Describe() == "second(wrapped(thing))");
    }

    /// <summary>
    /// A closed construction is decorated by naming it, which is what the generator emits for a
    /// generic decorator: one call per closed registration rather than one open-generic call.
    /// </summary>
    [VerifyTest]
    public void DecorateOfT_WrapsEachClosedConstructionIndependently()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepo<string>, StringRepo>();
        services.AddSingleton(typeof(IRepo<int>), typeof(Repo<int>));

        DecoratorHelper.Decorate<IRepo<string>>(
            services,
            typeof(RepoWrapper<string>),
            (_, inner) => new RepoWrapper<string>(inner)
        );
        DecoratorHelper.Decorate<IRepo<int>>(
            services,
            typeof(RepoWrapper<int>),
            (_, inner) => new RepoWrapper<int>(inner)
        );

        var provider = services.BuildServiceProvider();

        Verify(provider.GetRequiredService<IRepo<string>>().Describe() == "wrapped(repo)");
        Verify(provider.GetRequiredService<IRepo<int>>().Describe() == "wrapped(repo)");
    }

    /// <summary>
    /// The inner stays owned by the container here too.
    /// </summary>
    [VerifyTest]
    public void DecorateOfT_LeavesTheInnerImplementationOwnedByTheContainer()
    {
        var services = new ServiceCollection();
        services.AddScoped<IThing, DisposableThing>();

        DecoratorHelper.Decorate<IThing>(
            services,
            typeof(DisposableWrapper),
            (_, inner) => new DisposableWrapper(inner)
        );

        var provider = services.BuildServiceProvider();

        DisposableWrapper wrapper;

        using (var scope = provider.CreateScope())
        {
            wrapper = (DisposableWrapper)scope.ServiceProvider.GetRequiredService<IThing>();
        }

        Verify(((DisposableThing)wrapper.Inner).Disposals == 1);
    }
}

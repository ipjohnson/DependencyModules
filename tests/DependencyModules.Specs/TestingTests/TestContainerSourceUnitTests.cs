using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.TestingTests;

/// <summary>
/// The source driven directly, for the parts a test running through a framework cannot observe.
/// </summary>
public partial class TestContainerSourceUnitTests
{
    private interface IThing;

    private class Thing : IThing;

    private sealed class Tracked : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Harness
    {
        public List<IServiceProvider> Tracked { get; } = [];

        public List<IServiceProvider> Started { get; } = [];

        public TestContainerSource Source { get; } = new();

        public IServiceProvider Pin { get; }

        public Harness(Action<IServiceCollection> compose, params Type[] pinned)
            : this(compose, pinned, named: []) { }

        public Harness(Action<IServiceCollection> compose, Type[] pinned, Type[] named)
        {
            var services = new ServiceCollection();

            services.AddSingleton<ITestContainerSource>(Source);

            compose(services);

            Pin = services.BuildServiceProvider();

            Source.Initialize(
                services: services,
                pinned: Pin,
                pinnedServices: pinned,
                namedServices: named,
                build: collection => collection.BuildServiceProvider(),
                start: provider =>
                {
                    Started.Add(provider);

                    return ValueTask.CompletedTask;
                },
                track: Tracked.Add
            );
        }
    }

    /// <summary>
    /// Every container goes to the runner, which is what makes destroying them the runner's business
    /// rather than the caller's.
    /// </summary>
    [VerifyTest]
    public async Task EveryContainerBuiltIsHandedToTheRunner()
    {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Verify(harness.Tracked.SequenceEqual([first, second]));
    }

    /// <summary>
    /// Startup runs against each one. A container that skipped it is not the one the test composed.
    /// </summary>
    [VerifyTest]
    public async Task StartupRunsForEveryContainer()
    {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Verify(harness.Started.SequenceEqual([first, second]));
    }

    /// <summary>
    /// A pinned service survives its container being disposed, which is the whole reason the template
    /// uses an instance registration rather than a factory returning the same object.
    /// </summary>
    [VerifyTest]
    public async Task APinnedDisposableIsNotDisposedByTheContainersItIsHandedTo()
    {
        var tracked = new Tracked();

        var harness = new Harness(services => services.AddSingleton(_ => tracked), typeof(Tracked));

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Verify(ReferenceEquals(tracked, first.GetRequiredService<Tracked>()));
        Verify(ReferenceEquals(tracked, second.GetRequiredService<Tracked>()));

        ((IDisposable)first).Dispose();
        ((IDisposable)second).Dispose();

        Verify(tracked.Disposals == 0);
    }

    /// <summary>
    /// A type registered more than once keeps every registration, because collapsing the set to its
    /// last member would leave anything injecting the sequence one element long.
    /// </summary>
    [VerifyTest]
    public async Task PinningKeepsEveryRegistrationOfAService()
    {
        var harness = new Harness(
            services =>
            {
                services.AddSingleton<IThing, Thing>();
                services.AddSingleton<IThing, Thing>();
            },
            typeof(IThing)
        );

        var built = await harness.Source.CreateAsync();

        Verify(built.GetServices<IThing>().Count() == 2);
        Verify(built.GetServices<IThing>().SequenceEqual(harness.Pin.GetServices<IThing>()));
    }

    /// <summary>
    /// Pinning takes the instances from <c>IEnumerable&lt;T&gt;</c>, which holds no keyed
    /// registration. The keyed registrations of a pinned service stay as they were written.
    /// </summary>
    [VerifyTest]
    public async Task PinningKeepsTheKeyedRegistrationsOfAService()
    {
        var harness = new Harness(
            services =>
            {
                services.AddKeyedSingleton<IThing, Thing>("before");
                services.AddSingleton<IThing, Thing>();
                services.AddKeyedSingleton<IThing, Thing>("after");
            },
            typeof(IThing)
        );

        var built = await harness.Source.CreateAsync();

        Verify(
            ReferenceEquals(
                harness.Pin.GetRequiredService<IThing>(),
                built.GetRequiredService<IThing>()
            )
        );
        Verify(built.GetRequiredKeyedService<IThing>("before") is not null);
        Verify(built.GetRequiredKeyedService<IThing>("after") is not null);
    }

    /// <summary>
    /// A service an attribute names is one the test asked to share. When the test's container cannot
    /// produce it, every container built after it would get its own instance with no message.
    /// </summary>
    [VerifyTest]
    public async Task ANamedServiceThatCannotBeBuiltIsReported()
    {
        var harness = new Harness(
            services =>
                services.AddSingleton<IThing>(_ =>
                    throw new InvalidOperationException("cannot build it")
                ),
            pinned: [typeof(IThing)],
            named: [typeof(IThing)]
        );

        var refused = await ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await harness.Source.CreateAsync()
        );

        Verify(refused.Message.Contains(typeof(IThing).FullName!));
        Verify(refused.Message.Contains("cannot build it"));
        Verify(refused.InnerException?.Message == "cannot build it");
    }

    /// <summary>
    /// Nothing is built until something asks, so a test that never rebuilds pays for none of this.
    /// </summary>
    [VerifyTest]
    public void NothingIsBuiltUntilAsked()
    {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        Verify(harness.Tracked.Count == 0);
        Verify(harness.Started.Count == 0);
    }

    /// <summary>
    /// A source nobody initialized says so, rather than answering with a container built from
    /// nothing.
    /// </summary>
    [VerifyTest]
    public async Task AnUninitializedSourceRefuses()
    {
        var source = new TestContainerSource();

        var refused = await ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await source.CreateAsync()
        );

        Verify(refused.Message.Contains("never initialized"));
    }

    /// <summary>
    /// A pinned service the first container cannot produce is left alone rather than failing the
    /// build.
    /// </summary>
    /// <remarks>
    /// The case that made this necessary: a harness registers a deliberately failing factory for a
    /// parameter it cannot supply, so resolving it fails with a message naming the fix. Resolving
    /// eagerly here turned that message into a failure at container build for every test that took
    /// such a parameter and never resolved it.
    /// </remarks>
    [VerifyTest]
    public async Task APinnedServiceThatCannotBeBuiltIsLeftAlone()
    {
        var harness = new Harness(
            services =>
            {
                services.AddSingleton<IThing, Thing>();
                services.AddSingleton<string>(_ =>
                    throw new InvalidOperationException("name the fix")
                );
            },
            typeof(IThing),
            typeof(string)
        );

        var built = await harness.Source.CreateAsync();

        Verify(built.GetRequiredService<IThing>() is not null);

        var refused = ThrowsExactly<InvalidOperationException>(() =>
            built.GetRequiredService<string>()
        );

        Verify(refused.Message == "name the fix");
    }

    /// <summary>
    /// A pinned type nothing registered is inert, which is what lets the set be drawn from a
    /// signature without filtering it first.
    /// </summary>
    [VerifyTest]
    public async Task APinnedTypeNothingRegisteredIsInert()
    {
        var harness = new Harness(
            services => services.AddSingleton<IThing, Thing>(),
            typeof(IThing),
            typeof(int),
            typeof(Uri)
        );

        var built = await harness.Source.CreateAsync();

        Verify(built.GetRequiredService<IThing>() is not null);
        Verify(built.GetService<Uri>() is null);
    }
}

using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.TestingTests;

/// <summary>
/// The source driven directly, for the parts a test running through a framework cannot observe.
/// </summary>
public class TestContainerSourceUnitTests {

    private interface IThing;

    private class Thing : IThing;

    private sealed class Tracked : IDisposable {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Harness {
        public List<IServiceProvider> Tracked { get; } = [];

        public List<IServiceProvider> Started { get; } = [];

        public TestContainerSource Source { get; } = new();

        public IServiceProvider Pin { get; }

        public Harness(Action<IServiceCollection> compose, params Type[] pinned) {
            var services = new ServiceCollection();

            services.AddSingleton<ITestContainerSource>(Source);

            compose(services);

            Pin = services.BuildServiceProvider();

            Source.Initialize(
                services,
                Pin,
                pinned,
                collection => collection.BuildServiceProvider(),
                provider => {
                    Started.Add(provider);

                    return ValueTask.CompletedTask;
                },
                Tracked.Add);
        }
    }

    /// <summary>
    /// Every container goes to the runner, which is what makes destroying them the runner's business
    /// rather than the caller's.
    /// </summary>
    [Fact]
    public async Task EveryContainerBuiltIsHandedToTheRunner() {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Assert.Equal([first, second], harness.Tracked);
    }

    /// <summary>
    /// Startup runs against each one. A container that skipped it is not the one the test composed.
    /// </summary>
    [Fact]
    public async Task StartupRunsForEveryContainer() {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Assert.Equal([first, second], harness.Started);
    }

    /// <summary>
    /// A pinned service survives its container being disposed, which is the whole reason the template
    /// uses an instance registration rather than a factory returning the same object.
    /// </summary>
    [Fact]
    public async Task APinnedDisposableIsNotDisposedByTheContainersItIsHandedTo() {
        var tracked = new Tracked();

        var harness = new Harness(
            services => services.AddSingleton(_ => tracked),
            typeof(Tracked));

        var first = await harness.Source.CreateAsync();
        var second = await harness.Source.CreateAsync();

        Assert.Same(tracked, first.GetRequiredService<Tracked>());
        Assert.Same(tracked, second.GetRequiredService<Tracked>());

        ((IDisposable)first).Dispose();
        ((IDisposable)second).Dispose();

        Assert.Equal(0, tracked.Disposals);
    }

    /// <summary>
    /// A type registered more than once keeps every registration, because collapsing the set to its
    /// last member would leave anything injecting the sequence one element long.
    /// </summary>
    [Fact]
    public async Task PinningKeepsEveryRegistrationOfAService() {
        var harness = new Harness(
            services => {
                services.AddSingleton<IThing, Thing>();
                services.AddSingleton<IThing, Thing>();
            },
            typeof(IThing));

        var built = await harness.Source.CreateAsync();

        Assert.Equal(2, built.GetServices<IThing>().Count());
        Assert.Equal(
            harness.Pin.GetServices<IThing>(),
            built.GetServices<IThing>());
    }

    /// <summary>
    /// Nothing is built until something asks, so a test that never rebuilds pays for none of this.
    /// </summary>
    [Fact]
    public void NothingIsBuiltUntilAsked() {
        var harness = new Harness(services => services.AddSingleton<IThing, Thing>());

        Assert.Empty(harness.Tracked);
        Assert.Empty(harness.Started);
    }

    /// <summary>
    /// A source nobody initialized says so, rather than answering with a container built from
    /// nothing.
    /// </summary>
    [Fact]
    public async Task AnUninitializedSourceRefuses() {
        var source = new TestContainerSource();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.CreateAsync());

        Assert.Contains("never initialized", refused.Message);
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
    [Fact]
    public async Task APinnedServiceThatCannotBeBuiltIsLeftAlone() {
        var harness = new Harness(
            services => {
                services.AddSingleton<IThing, Thing>();
                services.AddSingleton<string>(_ => throw new InvalidOperationException("name the fix"));
            },
            typeof(IThing), typeof(string));

        var built = await harness.Source.CreateAsync();

        Assert.NotNull(built.GetRequiredService<IThing>());

        var refused = Assert.Throws<InvalidOperationException>(() => built.GetRequiredService<string>());

        Assert.Equal("name the fix", refused.Message);
    }

    /// <summary>
    /// A pinned type nothing registered is inert, which is what lets the set be drawn from a
    /// signature without filtering it first.
    /// </summary>
    [Fact]
    public async Task APinnedTypeNothingRegisteredIsInert() {
        var harness = new Harness(
            services => services.AddSingleton<IThing, Thing>(),
            typeof(IThing), typeof(int), typeof(Uri));

        var built = await harness.Source.CreateAsync();

        Assert.NotNull(built.GetRequiredService<IThing>());
        Assert.Null(built.GetService<Uri>());
    }
}


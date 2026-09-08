using System.Collections;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// The runner's <see cref="ITestContainerSource"/>: builds a container per call from a template
/// taken once off the test's own composition.
/// </summary>
/// <remarks>
/// <para>
/// Registered into the collection before the test's container is built, because anything that wants
/// one resolves it like any other service, and filled in afterwards - the source cannot describe how
/// to rebuild a container until the first one exists to take the pinned instances from. Registered as
/// an instance, so it belongs to the test case rather than to any container and is never disposed by
/// one.
/// </para>
/// <para>
/// <b>Nothing happens until something asks.</b> The template is built on the first
/// <see cref="CreateAsync"/> and not before, so a test that never rebuilds - which is nearly all of
/// them - pays for none of this.
/// </para>
/// </remarks>
public sealed class TestContainerSource : ITestContainerSource {
    private readonly object _gate = new();

    private Composition? _composition;
    private IServiceCollection? _template;

    /// <summary>
    /// What the runner knows and this does not, handed over once the test's container exists.
    /// </summary>
    /// <param name="services">The collection the test's container was built from.</param>
    /// <param name="pinned">The container to take pinned instances out of.</param>
    /// <param name="pinnedServices">The service types to keep, from <see cref="SharedRegistrations"/>.</param>
    /// <param name="build">Builds a provider the same way the runner built the first one.</param>
    /// <param name="start">Runs the test's startup attributes against a newly built provider.</param>
    /// <param name="track">Hands a built provider to the runner, which disposes it when the case has run.</param>
    public void Initialize(
        IServiceCollection services,
        IServiceProvider pinned,
        IReadOnlyCollection<Type> pinnedServices,
        Func<IServiceCollection, IServiceProvider> build,
        Func<IServiceProvider, ValueTask> start,
        Action<IServiceProvider> track) {
        _composition = new Composition(services, pinned, pinnedServices, build, start, track);
    }

    /// <inheritdoc />
    public async ValueTask<IServiceProvider> CreateAsync() {
        var composition = _composition
                          ?? throw new InvalidOperationException(
                              $"This {nameof(TestContainerSource)} was never initialized, so there is " +
                              "nothing to build a container from. The runner does that once the test's " +
                              "own container exists.");

        var provider = composition.Build(Template(composition));

        composition.Track(provider);

        await composition.Start(provider);

        return provider;
    }

    /// <remarks>
    /// Under a lock because a test is free to drive two calls at once, and building the template
    /// twice would take a second set of pinned instances out of the first container - leaving two
    /// objects where the whole point is one.
    /// </remarks>
    private IServiceCollection Template(Composition composition) {
        if (_template != null) {
            return _template;
        }

        lock (_gate) {
            return _template ??= BuildTemplate(composition);
        }
    }

    /// <summary>
    /// The test's collection with every pinned service replaced by the instance the first container
    /// produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An instance registration rather than a factory returning the instance</b>, which is the
    /// difference between a shared object surviving the test and being disposed once per container.
    /// A provider disposes what it created, and a factory registration counts as created; an instance
    /// it was handed does not. The first disposal would otherwise land while other containers were
    /// still running.
    /// </para>
    /// <para>
    /// Resolved through <c>IEnumerable&lt;T&gt;</c> rather than as a single service, so a type
    /// registered more than once keeps every registration and its order. Taking the single service
    /// would collapse the set to its last member and leave anything injecting the sequence one
    /// element long.
    /// </para>
    /// <para>
    /// A descriptor that already carries an instance is left alone: it is the same object in every
    /// container built from this collection already, which is how the module environment is shared
    /// without anyone asking. An open generic is left alone too, having no closed type to resolve.
    /// </para>
    /// </remarks>
    private static IServiceCollection BuildTemplate(Composition composition) {
        IServiceCollection template = new ServiceCollection();
        var taken = new HashSet<Type>();

        foreach (var descriptor in composition.Services) {
            var serviceType = descriptor.ServiceType;

            if (!composition.PinnedServices.Contains(serviceType) ||
                descriptor.ImplementationInstance != null ||
                serviceType.IsGenericTypeDefinition) {
                template.Add(descriptor);

                continue;
            }

            if (!taken.Add(serviceType)) {
                continue;
            }

            var sequence = typeof(IEnumerable<>).MakeGenericType(serviceType);

            foreach (var instance in (IEnumerable)composition.Pinned.GetRequiredService(sequence)) {
                if (instance != null) {
                    template.Add(new ServiceDescriptor(serviceType, instance));
                }
            }
        }

        return template;
    }

    private sealed record Composition(
        IServiceCollection Services,
        IServiceProvider Pinned,
        IReadOnlyCollection<Type> PinnedServices,
        Func<IServiceCollection, IServiceProvider> Build,
        Func<IServiceProvider, ValueTask> Start,
        Action<IServiceProvider> Track);
}

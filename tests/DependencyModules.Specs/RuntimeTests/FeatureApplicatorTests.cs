using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

/// <summary>
/// FeatureApplicator is the bridge between a module implementing IDependencyModuleFeature and the
/// modules carrying that feature. It must hand the handler exactly the modules that implement the
/// feature type, and nothing else.
/// </summary>
public partial class FeatureApplicatorTests
{
    private interface ISomeFeature;

    [VerifyTest]
    public void Apply_PassesOnlyModulesImplementingTheFeature()
    {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);

        var featured = new FeatureModule();
        var plain = new PlainModule();

        applicator.Apply(new ServiceCollection(), [featured, plain]);

        var received = Single(handler.Received!);
        Verify(ReferenceEquals(received, featured));
    }

    [VerifyTest]
    public void Apply_WithNoMatchingModules_PassesAnEmptySequence()
    {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);

        applicator.Apply(new ServiceCollection(), [new PlainModule()]);

        Verify(!handler.Received!.Any());
    }

    [VerifyTest]
    public void Apply_PassesTheServiceCollectionThrough()
    {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);
        var collection = new ServiceCollection();

        applicator.Apply(collection, [new FeatureModule()]);

        Verify(ReferenceEquals(handler.ReceivedCollection, collection));
    }

    [VerifyTest]
    public void Order_ComesFromTheHandler()
    {
        var applicator = new FeatureApplicator<ISomeFeature>(
            new RecordingHandler { HandlerOrder = 42 }
        );

        Verify(applicator.Order == 42);
    }

    [VerifyTest]
    public void Order_DefaultsToZero()
    {
        Verify(new FeatureApplicator<ISomeFeature>(new DefaultOrderHandler()).Order == 0);
    }

    private class RecordingHandler : IDependencyModuleFeature<ISomeFeature>
    {
        public int HandlerOrder { get; init; }

        public int Order => HandlerOrder;

        public IEnumerable<ISomeFeature>? Received { get; private set; }

        public IServiceCollection? ReceivedCollection { get; private set; }

        public void HandleFeature(IServiceCollection collection, IEnumerable<ISomeFeature> feature)
        {
            ReceivedCollection = collection;
            Received = feature.ToList();
        }
    }

    private class DefaultOrderHandler : IDependencyModuleFeature<ISomeFeature>
    {
        public void HandleFeature(
            IServiceCollection collection,
            IEnumerable<ISomeFeature> feature
        ) { }
    }

    private class FeatureModule : IDependencyModule, ISomeFeature
    {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }

    private class PlainModule : IDependencyModule
    {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }
}

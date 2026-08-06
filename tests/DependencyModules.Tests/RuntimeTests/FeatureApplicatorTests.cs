using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// FeatureApplicator is the bridge between a module implementing IDependencyModuleFeature and the
/// modules carrying that feature. It must hand the handler exactly the modules that implement the
/// feature type, and nothing else.
/// </summary>
public class FeatureApplicatorTests {

    private interface ISomeFeature;

    [Fact]
    public void Apply_PassesOnlyModulesImplementingTheFeature() {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);

        var featured = new FeatureModule();
        var plain = new PlainModule();

        applicator.Apply(new ServiceCollection(), [featured, plain]);

        var received = Assert.Single(handler.Received!);
        Assert.Same(featured, received);
    }

    [Fact]
    public void Apply_WithNoMatchingModules_PassesAnEmptySequence() {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);

        applicator.Apply(new ServiceCollection(), [new PlainModule()]);

        Assert.Empty(handler.Received!);
    }

    [Fact]
    public void Apply_PassesTheServiceCollectionThrough() {
        var handler = new RecordingHandler();
        var applicator = new FeatureApplicator<ISomeFeature>(handler);
        var collection = new ServiceCollection();

        applicator.Apply(collection, [new FeatureModule()]);

        Assert.Same(collection, handler.ReceivedCollection);
    }

    [Fact]
    public void Order_ComesFromTheHandler() {
        var applicator = new FeatureApplicator<ISomeFeature>(new RecordingHandler { HandlerOrder = 42 });

        Assert.Equal(42, applicator.Order);
    }

    [Fact]
    public void Order_DefaultsToZero() {
        Assert.Equal(0, new FeatureApplicator<ISomeFeature>(new DefaultOrderHandler()).Order);
    }

    private class RecordingHandler : IDependencyModuleFeature<ISomeFeature> {
        public int HandlerOrder { get; init; }

        public int Order => HandlerOrder;

        public IEnumerable<ISomeFeature>? Received { get; private set; }

        public IServiceCollection? ReceivedCollection { get; private set; }

        public void HandleFeature(IServiceCollection collection, IEnumerable<ISomeFeature> feature) {
            ReceivedCollection = collection;
            Received = feature.ToList();
        }
    }

    private class DefaultOrderHandler : IDependencyModuleFeature<ISomeFeature> {
        public void HandleFeature(IServiceCollection collection, IEnumerable<ISomeFeature> feature) { }
    }

    private class FeatureModule : IDependencyModule, ISomeFeature {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }

    private class PlainModule : IDependencyModule {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
    }
}

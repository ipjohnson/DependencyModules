using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

/// <summary>
/// Represents an applicator for handling dependency module features.
/// This class is responsible for applying a specific feature type to a service collection
/// in conjunction with a collection of dependency modules.
/// </summary>
/// <typeparam name="TFeature">
/// The type of the feature being applied, which must adhere to the constraints defined
/// by the corresponding feature handler.
/// </typeparam>
public class FeatureApplicator<TFeature>(IDependencyModuleFeature<TFeature> handler) : IFeatureApplicator {
    /// <summary>
    /// Gets the order of the feature applicator execution.
    /// The order determines the sequence in which feature applicators
    /// are evaluated and applied when handling dependency module features.
    /// </summary>
    public int Order => handler.Order;

    /// <summary>
    /// Applies a collection of dependency modules and their features to a service collection.
    /// </summary>
    /// <param name="serviceCollection">
    /// The service collection to which the feature is applied.
    /// </param>
    /// <param name="modules">
    /// The list of dependency modules containing features to be applied.
    /// </param>
    public void Apply(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules) {
        handler.HandleFeature(serviceCollection, modules.OfType<TFeature>());
    }
}
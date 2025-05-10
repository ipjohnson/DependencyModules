using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

/// <summary>
/// Defines a feature handler for dependency injection modules.
/// This interface allows processing of certain feature types and adding corresponding services to the
/// application service collection during startup.
/// </summary>
/// <typeparam name="TFeature">
/// The feature type to be handled by the module. Represents objects or details that can be utilized
/// to configure or apply specific functionality during service registration.
/// </typeparam>
public interface IDependencyModuleFeature<in TFeature> {
    /// <summary>
    /// Gets the order in which the dependency module feature should be applied.
    /// Features with a lower order value are handled earlier during the service collection
    /// configuration process. This allows for controlling the sequence in which modules are processed,
    /// ensuring that dependent modules or services are registered in the correct order.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Handles the provided features and applies them to the given service collection. This method
    /// is responsible for configuring and registering services based on the provided features.
    /// Implements functionality based on the <see cref="IDependencyModuleFeature{TFeature}"/> interface.
    /// </summary>
    /// <param name="collection">
    /// The <see cref="IServiceCollection"/> instance used to register the configured services.
    /// Services are added to this collection during feature processing.
    /// </param>
    /// <param name="feature">
    /// An <see cref="IEnumerable{TFeature}"/> representing the features to be processed.
    /// Each feature provides specific information to configure related services.
    /// </param>
    void HandleFeature(IServiceCollection collection, IEnumerable<TFeature> feature);
}
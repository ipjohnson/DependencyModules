using DependencyModules.Runtime.Features;

namespace DependencyModules.Runtime.Features;

/// <summary>
/// Provides a mechanism to retrieve a collection of feature applicators used for handling
/// and applying specific features in the dependency module system.
/// </summary>
public interface IDependencyModuleApplicatorProvider {

    /// <summary>
    /// Retrieves a collection of feature applicators responsible for handling and applying
    /// specific features in the dependency module system.
    /// </summary>
    /// <returns>
    /// An enumerable collection of objects implementing the <see cref="IFeatureApplicator"/> interface.
    /// If no feature applicators are available, an empty collection is returned.
    /// </returns>
    IEnumerable<IFeatureApplicator> FeatureApplicators() {
        return ArraySegment<IFeatureApplicator>.Empty;
    }
}
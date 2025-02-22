using DependencyModules.Runtime.Features;

namespace DependencyModules.Runtime.Features;

public interface IDependencyModuleApplicatorProvider {

    IEnumerable<IFeatureApplicator> FeatureApplicators() {
        return ArraySegment<IFeatureApplicator>.Empty;
    }
}
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Features;

/// <summary>
/// Defines the contract for a feature applicator responsible for applying
/// specific features within a dependency management system.
/// </summary>
public interface IFeatureApplicator {
    /// <summary>
    /// Represents the order of execution for feature applicators when applying features
    /// to a service collection. This property determines the sequence in which
    /// feature applicators are executed, with lower values indicating earlier execution.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Applies dependency modules and their respective features to a service collection.
    /// </summary>
    /// <param name="serviceCollection">
    /// The service collection to which the features are applied.
    /// </param>
    /// <param name="modules">
    /// A read-only list of dependency modules containing the features to be applied.
    /// </param>
    void Apply(IServiceCollection serviceCollection, IReadOnlyList<IDependencyModule> modules);
}
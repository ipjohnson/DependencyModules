using System.ComponentModel;
using DependencyModules.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     Internal interface not intended to be consumed by developers
/// </summary>
public interface IDependencyModule {
    /// <summary>
    /// Flag to disable loading module and dependencies.
    /// </summary>
    bool LoadModule => true;
    
    /// <summary>
    /// Populate a service collection with registrations
    /// </summary>
    /// <param name="serviceCollection"></param>
    void PopulateServiceCollection(IServiceCollection serviceCollection);

    /// <summary>
    /// Intended for developers to override and provide their own IDependencyModules
    /// </summary>
    /// <returns></returns>
    IEnumerable<IDependencyModule> GetModules() {
        return ArraySegment<IDependencyModule>.Empty;
    }
    
    /// <summary>
    /// Internal method not intended to be called by general developers
    /// </summary>
    /// <returns></returns>
    [Browsable(false)]
    IEnumerable<object> InternalGetModules() {
        return ArraySegment<IDependencyModule>.Empty;
    }

    /// <summary>
    /// Internal method not intended to be called by general developers
    /// </summary>
    /// <param name="serviceCollection"></param>
    [Browsable(false)]
    void InternalApplyServices(IServiceCollection serviceCollection) { }

    
    /// <summary>
    /// Internal method not intended to be called by general developers
    /// </summary>
    /// <param name="serviceCollection"></param>
    [Browsable(false)]
    void InternalApplyDecorators(IServiceCollection serviceCollection) { }
}
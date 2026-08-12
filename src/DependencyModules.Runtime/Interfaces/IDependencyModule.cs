using System.ComponentModel;
using DependencyModules.Runtime.Features;
using DependencyModules.Runtime.Helpers;
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
        return Array.Empty<IDependencyModule>();
    }
    
    /// <summary>
    /// Internal method not intended to be called by general developers
    /// </summary>
    /// <returns></returns>
    [Browsable(false)]
    IEnumerable<object> InternalGetModules() {
        // Array.Empty<object>() rather than an array of the interface, so the runtime's empty check
        // is a plain ICollection<object> test rather than one relying on array covariance.
        return Array.Empty<object>();
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
    /// <remarks>
    /// The overload generated modules implement once any of their registrations carries an
    /// environment condition. It forwards to the collection-only overload by default, so a module
    /// compiled against an earlier generator keeps registering exactly what it did before.
    /// </remarks>
    /// <param name="serviceCollection"></param>
    /// <param name="environment">Never null; see <c>ModuleEnvironment.Default</c>.</param>
    [Browsable(false)]
    void InternalApplyServices(IServiceCollection serviceCollection, IModuleEnvironment environment) {
        InternalApplyServices(serviceCollection);
    }


    /// <summary>
    /// Internal method not intended to be called by general developers
    /// </summary>
    /// <param name="serviceCollection"></param>
    [Browsable(false)]
    void InternalApplyDecorators(IServiceCollection serviceCollection) { }

    /// <summary>
    /// Internal method not intended to be called by general developers.
    /// </summary>
    /// <remarks>
    /// Returning decorators rather than applying them lets the runtime sort every module's
    /// decorators together. Applying them per module would make module discovery order outrank the
    /// order the developer declared.
    /// </remarks>
    [Browsable(false)]
    IEnumerable<DecoratorRegistration> InternalGetDecorators() {
        return Array.Empty<DecoratorRegistration>();
    }
}
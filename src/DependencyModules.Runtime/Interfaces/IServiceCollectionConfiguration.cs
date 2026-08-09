using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Interfaces;

/// <summary>
///     DependencyModules that want to do programmatic registration should implement this interface.
/// </summary>
public interface IServiceCollectionConfiguration {
    /// <summary>
    /// Configure service in IServiceCollection
    /// </summary>
    /// <param name="services"></param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Called after all services have been registered allowing for decorating
    /// </summary>
    /// <param name="services"></param>
    void ConfigureDecorators(IServiceCollection services) { }
}

/// <summary>
///     DependencyModules that need access to the environment during registration should implement this interface.
/// </summary>
public interface IEnvironmentServiceCollectionConfiguration {
    /// <summary>
    /// Configure services with access to the module environment.
    /// </summary>
    /// <remarks>
    /// The environment is never null. An application that supplies none gets
    /// <c>ModuleEnvironment.Default</c>, read from the process, and one that has no environment says
    /// so with <c>ModuleEnvironment.None</c>. That is the same environment the
    /// <c>[IfEnvironment]</c> attributes are evaluated against, so a module cannot see one answer
    /// here and a different one in its own conditional registrations.
    /// </remarks>
    /// <param name="services"></param>
    /// <param name="environment">The environment for this AddModules call. Never null.</param>
    void ConfigureServices(IServiceCollection services, IModuleEnvironment environment);
}
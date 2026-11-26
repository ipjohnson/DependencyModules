using DependencyModules.Runtime.Interfaces;
using DependencyModules.VerificationGlue.Impl;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue;

/// <summary>
/// Builds a test container from the module attributes in the scope.
/// <see cref="Attributes.ModuleTestAttribute"/> names it.
/// </summary>
/// <remarks>
/// Implements <see cref="ISupportsChildOverrides"/>: a test under a class or assembly container
/// gets a container of its own, which returns the test's <c>[Mock]</c> parameters.
/// </remarks>
public sealed class ModuleBootstrap : ITestContainerBootstrap, ISupportsChildOverrides
{
    /// <inheritdoc />
    public static ValueTask<ITestContainer> BuildAsync(TestContainerContext context) =>
        ModuleContainers.BuildAsync(context, static () => []);
}

/// <summary>
/// Builds a test container from <typeparamref name="TModule"/> and the module attributes in the
/// scope. <see cref="Attributes.ModuleTestAttribute{TModule}"/> names it.
/// </summary>
/// <remarks>
/// A separate type for each module, so two classes that share an assembly container with
/// different modules get two containers. VerificationGlue.Tests shares an assembly container
/// between the classes that name the same bootstrap.
/// </remarks>
/// <typeparam name="TModule">The module to load first.</typeparam>
public sealed class ModuleBootstrap<TModule> : ITestContainerBootstrap, ISupportsChildOverrides
    where TModule : IDependencyModule, new()
{
    /// <inheritdoc />
    public static ValueTask<ITestContainer> BuildAsync(TestContainerContext context) =>
        ModuleContainers.BuildAsync(context, static () => [new TModule()]);
}

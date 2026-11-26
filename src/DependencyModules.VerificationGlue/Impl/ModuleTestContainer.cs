using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// The container of one test.
/// </summary>
/// <remarks>
/// It disposes its service provider and the unregistered concrete types it built for parameters.
/// The overrides are instance registrations, so the provider does not dispose them.
/// </remarks>
internal sealed class ModuleTestContainer(
    IServiceProvider services,
    VerificationGlueTestMethodContext methodContext
) : ITestContainer
{
    private readonly List<object> _created = [];

    public IServiceProvider Services => services;

    public bool TryResolve(TestParameter parameter, out object? value) =>
        ParameterResolution.TryResolve(services, parameter, methodContext, Track, out value);

    /// <remarks>
    /// VerificationGlue.Tests asks for a child only of a class or assembly container.
    /// </remarks>
    public ValueTask<ITestContainer> CreateChildAsync(TestContainerContext context) =>
        throw new InvalidOperationException(
            "A test container has no children. Only a class or assembly container does."
        );

    public async ValueTask DisposeAsync()
    {
        object[] created;

        lock (_created)
        {
            created = _created.ToArray();
            _created.Clear();
        }

        foreach (var instance in created)
        {
            await ModuleContainers.DisposeAsync(instance);
        }

        await ModuleContainers.DisposeAsync(services);
    }

    private void Track(object instance)
    {
        lock (_created)
        {
            _created.Add(instance);
        }
    }
}

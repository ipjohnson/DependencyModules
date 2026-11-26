using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// Builds the service collection and the service provider of a module container.
/// </summary>
/// <remarks>
/// The steps and their order are the ones that DependencyModules.xUnit and DependencyModules.NUnit
/// use, so an attribute means the same thing under each runner. The test's overrides are
/// registered last, so a <c>[Mock]</c> parameter replaces a <c>[TestExport]</c> of the same
/// service.
/// </remarks>
internal static class ModuleContainers
{
    /// <summary>
    /// A test container for a test scope, or a shared container for a class or assembly scope.
    /// </summary>
    public static async ValueTask<ITestContainer> BuildAsync(
        TestContainerContext context,
        Func<IDependencyModule[]> modules
    )
    {
        if (context.Scope.Kind == TestScopeKind.Test)
        {
            return await BuildTestContainerAsync(context, modules, shared: null);
        }

        return SharedModuleContainer.Build(context, modules);
    }

    /// <summary>
    /// The container of one test. <paramref name="shared"/> is the class or assembly container it
    /// is a child of, or null.
    /// </summary>
    public static async ValueTask<ITestContainer> BuildTestContainerAsync(
        TestContainerContext context,
        Func<IDependencyModule[]> modules,
        SharedModuleContainer? shared
    )
    {
        var methodContext = new VerificationGlueTestMethodContext(context);
        var attributes = context.Attributes;
        var services = new ServiceCollection();

        var currentTest = new VerificationGlueCurrentTestProvider(context, methodContext);
        services.AddSingleton<ICurrentTestProvider>(currentTest);
        services.AddSingleton(context.Output);

        SeedEnvironment(services, attributes, methodContext);
        LoadModules(services, attributes, modules());
        SetupServices(services, attributes, methodContext);

        shared?.AddSharedInstances(services, context);

        AddOverrides(services, context.Overrides);

        var provider = BuildProvider(services, attributes, methodContext);

        try
        {
            foreach (var startup in attributes.OfType<ITestStartupAttribute>())
            {
                await startup.StartupAsync(methodContext, provider);
            }
        }
        catch
        {
            await DisposeAsync(provider);
            throw;
        }

        return new ModuleTestContainer(provider, methodContext);
    }

    /// <summary>
    /// The service collection of a class or assembly container, which has no test method and no
    /// overrides.
    /// </summary>
    public static IServiceProvider BuildSharedProvider(
        TestContainerContext context,
        IDependencyModule[] modules
    )
    {
        var methodContext = new VerificationGlueTestMethodContext(context);
        var services = new ServiceCollection();

        LoadModules(services, context.Attributes, modules);
        SetupServices(services, context.Attributes, methodContext);

        return BuildProvider(services, context.Attributes, methodContext);
    }

    /// <remarks>
    /// A test scope only. An <see cref="IModuleEnvironmentProvider"/> takes the test method, and a
    /// class or assembly container is built for no single method.
    /// </remarks>
    private static void SeedEnvironment(
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes,
        VerificationGlueTestMethodContext methodContext
    )
    {
        IModuleEnvironment? environment = null;

        foreach (var provider in attributes.OfType<IModuleEnvironmentProvider>())
        {
            environment = provider.ProvideEnvironment(methodContext.Method) ?? environment;
        }

        if (environment != null)
        {
            services.Add(new ServiceDescriptor(typeof(IModuleEnvironment), environment));
        }
    }

    /// <remarks>
    /// The same order as DependencyModules.xUnit: the bootstrap's modules, then the module
    /// attributes widest scope first, and the whole list reversed.
    /// </remarks>
    private static void LoadModules(
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes,
        IDependencyModule[] bootstrapModules
    )
    {
        var modules = new List<IDependencyModule>(bootstrapModules);

        foreach (var provider in attributes.OfType<IDependencyModuleProvider>())
        {
            modules.Add(provider.GetModule());
        }

        modules.Reverse();

        DependencyRegistry<object>.LoadModules(services, modules.ToArray());
    }

    /// <remarks>
    /// Mock support first, so that a <c>[TestExport]</c> replaces a mock library's registration
    /// wherever the two attributes are. DependencyModules.xUnit does the same.
    /// </remarks>
    private static void SetupServices(
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes,
        ITestMethodContext methodContext
    )
    {
        var setupAttributes = attributes
            .OfType<ITestServiceSetupAttribute>()
            .OrderBy(attribute => attribute is IMockSupportAttribute ? 0 : 1);

        foreach (var setupAttribute in setupAttributes)
        {
            setupAttribute.SetupServiceCollection(methodContext, services);
        }
    }

    /// <summary>
    /// Registers each override as an instance, so the container returns it and does not dispose
    /// it. A keyed override is registered under its key.
    /// </summary>
    private static void AddOverrides(
        IServiceCollection services,
        IReadOnlyList<TestOverride> overrides
    )
    {
        foreach (var entry in overrides)
        {
            if (entry.Key is null)
            {
                services.AddSingleton(entry.ServiceType, entry.Instance);
            }
            else
            {
                services.AddKeyedSingleton(entry.ServiceType, entry.Key, entry.Instance);
            }
        }
    }

    /// <remarks>
    /// The narrowest <see cref="IServiceProviderBuilderAttribute"/>, which is the last one because
    /// VerificationGlue.Tests gives the attributes widest scope first.
    /// </remarks>
    private static IServiceProvider BuildProvider(
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes,
        ITestMethodContext methodContext
    )
    {
        var builder = attributes.OfType<IServiceProviderBuilderAttribute>().LastOrDefault();

        return builder?.BuildServiceProvider(methodContext, services)
            ?? services.BuildServiceProvider();
    }

    public static async ValueTask DisposeAsync(object? instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}

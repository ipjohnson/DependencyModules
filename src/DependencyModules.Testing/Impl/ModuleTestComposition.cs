using System.Reflection;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// Builds a test's container from modules and testing attributes, the same way for every runner.
/// </summary>
public static class ModuleTestComposition
{
    /// <summary>
    /// Builds the container of one test.
    /// </summary>
    /// <remarks>
    /// <para>The steps, in order:</para>
    /// <list type="number">
    /// <item>Register an <see cref="ITestContainerSource"/>.</item>
    /// <item>Register <paramref name="currentTest"/> when it is not null.</item>
    /// <item>Register the environment from the narrowest <see cref="IModuleEnvironmentProvider"/> that answers.</item>
    /// <item>Load <paramref name="modules"/>, reversed.</item>
    /// <item>Run the <see cref="ITestServiceSetupAttribute"/>s, the <see cref="IMockSupportAttribute"/>s first.</item>
    /// <item>When <paramref name="parent"/> is set, register the parent's instances of the shared services.</item>
    /// <item>Run <see cref="ITestParameterValueProvider.SetupServiceCollection"/> for each parameter that no override names.</item>
    /// <item>Register <paramref name="overrides"/> as instances.</item>
    /// <item>Build with the narrowest <see cref="IServiceProviderBuilderAttribute"/>, or <c>BuildServiceProvider()</c>.</item>
    /// <item>Run the <see cref="ITestStartupAttribute"/>s.</item>
    /// <item>Initialize the <see cref="TestContainerSource"/>.</item>
    /// </list>
    /// <para>
    /// When a step throws, every container built so far is disposed and the exception is rethrown.
    /// </para>
    /// </remarks>
    /// <param name="services">
    /// The collection to build into. A runner registers its own services in it first.
    /// </param>
    /// <param name="modules">
    /// The modules in declaration order: the runner's own modules first, then the module attributes,
    /// widest scope first.
    /// </param>
    /// <param name="testMethod">The test method and its attribute instances, widest scope first.</param>
    /// <param name="overrides">The instances that replace services, such as mocks.</param>
    /// <param name="currentTest">
    /// Registered as the container's <see cref="ICurrentTestProvider"/> when it is not null.
    /// <see cref="CurrentTest.Provider"/> is not changed.
    /// </param>
    /// <param name="parent">
    /// A class or assembly container from <see cref="BuildScopeAsync"/>, or null.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The instance of an override is not assignable to its service type.
    /// </exception>
    public static async ValueTask<ModuleTestContainer> BuildAsync(
        IServiceCollection services,
        IReadOnlyList<IDependencyModule> modules,
        ITestMethodContext testMethod,
        IReadOnlyList<TestServiceOverride> overrides,
        ICurrentTestProvider? currentTest,
        ModuleTestContainer? parent
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(testMethod);
        ArgumentNullException.ThrowIfNull(overrides);

        foreach (var entry in overrides)
        {
            if (!entry.ServiceType.IsInstanceOfType(entry.Instance))
            {
                throw new ArgumentException(
                    $"The override for {entry.ServiceType} is a {entry.Instance.GetType()}, which is "
                        + $"not a {entry.ServiceType}.",
                    nameof(overrides)
                );
            }
        }

        var attributes = testMethod.Attributes;
        var resolver = new TestParameterResolver(
            testMethod,
            parameter => IsOverridden(parameter, overrides)
        );
        var container = new ModuleTestContainer(resolver, attributes);

        try
        {
            var containerSource = new TestContainerSource();

            services.AddSingleton<ITestContainerSource>(containerSource);

            if (currentTest != null)
            {
                services.AddSingleton(currentTest);
            }

            SeedEnvironment(services, testMethod.Method, attributes);

            LoadModules(services, modules);

            SetupServiceSetupAttributes(testMethod, services, attributes);

            parent?.RegisterSharedServices(services, testMethod);

            resolver.SetupServiceCollection(services);

            foreach (var entry in overrides)
            {
                services.Add(
                    entry.ServiceKey == null
                        ? new ServiceDescriptor(entry.ServiceType, entry.Instance)
                        : new ServiceDescriptor(entry.ServiceType, entry.ServiceKey, entry.Instance)
                );
            }

            var provider = BuildServiceProvider(testMethod, services, attributes);

            container.SetServices(provider);

            await StartAsync(testMethod, attributes, provider);

            containerSource.Initialize(
                services: services,
                pinned: provider,
                pinnedServices: SharedRegistrations.Collect(testMethod.Method, attributes),
                namedServices: SharedRegistrations.CollectNamed(testMethod.Method, attributes),
                build: built => BuildServiceProvider(testMethod, built, attributes),
                start: built => StartAsync(testMethod, attributes, built),
                track: container.Track
            );

            return container;
        }
        catch
        {
            try
            {
                await container.DisposeAsync();
            }
            catch
            {
                // The exception of the step that failed says more than one from the disposal.
            }

            throw;
        }
    }

    /// <summary>
    /// Builds the shared container of a class or an assembly, which <see cref="BuildAsync"/> takes
    /// as its parent.
    /// </summary>
    /// <remarks>
    /// It loads <paramref name="modules"/>, reversed, with the default environment. It registers
    /// each <c>[TestExport(Shared = true)]</c> in <paramref name="scopeAttributes"/> and builds with
    /// <c>BuildServiceProvider()</c>. It runs nothing that needs a test method.
    /// </remarks>
    /// <param name="services">The collection to build into.</param>
    /// <param name="modules">The modules in declaration order, as for <see cref="BuildAsync"/>.</param>
    /// <param name="scopeAttributes">The attribute instances of the class or the assembly, widest scope first.</param>
    public static ValueTask<ModuleTestContainer> BuildScopeAsync(
        IServiceCollection services,
        IReadOnlyList<IDependencyModule> modules,
        IReadOnlyList<Attribute> scopeAttributes
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(scopeAttributes);

        LoadModules(services, modules);

        foreach (var export in scopeAttributes.OfType<TestExportAttribute>())
        {
            if (export.Shared)
            {
                export.Register(services);
            }
        }

        var container = new ModuleTestContainer(null, scopeAttributes);

        container.SetServices(services.BuildServiceProvider());

        return new ValueTask<ModuleTestContainer>(container);
    }

    private static bool IsOverridden(
        ParameterInfo parameter,
        IReadOnlyList<TestServiceOverride> overrides
    )
    {
        var key = parameter.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key;

        return overrides.Any(entry =>
            entry.ServiceType == parameter.ParameterType && Equals(entry.ServiceKey, key)
        );
    }

    /// <remarks>
    /// Before the modules, because <c>LoadModules</c> decides <c>[IfEnvironment]</c> from the
    /// <see cref="IModuleEnvironment"/> already in the collection.
    /// </remarks>
    private static void SeedEnvironment(
        IServiceCollection services,
        MethodInfo method,
        IReadOnlyList<Attribute> attributes
    )
    {
        IModuleEnvironment? environment = null;

        foreach (var provider in attributes.OfType<IModuleEnvironmentProvider>())
        {
            environment = provider.ProvideEnvironment(method) ?? environment;
        }

        if (environment != null)
        {
            services.Add(new ServiceDescriptor(typeof(IModuleEnvironment), environment));
        }
    }

    private static void LoadModules(
        IServiceCollection services,
        IReadOnlyList<IDependencyModule> modules
    )
    {
        var reversed = modules.ToArray();

        Array.Reverse(reversed);

        DependencyRegistry<object>.LoadModules(services, reversed);
    }

    private static void SetupServiceSetupAttributes(
        ITestMethodContext testMethod,
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes
    )
    {
        var setupAttributes = attributes
            .OfType<ITestServiceSetupAttribute>()
            .OrderBy(attribute => attribute is IMockSupportAttribute ? 0 : 1);

        foreach (var setupAttribute in setupAttributes)
        {
            setupAttribute.SetupServiceCollection(testMethod, services);
        }
    }

    private static IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod,
        IServiceCollection services,
        IReadOnlyList<Attribute> attributes
    )
    {
        var builder = attributes.OfType<IServiceProviderBuilderAttribute>().LastOrDefault();

        return builder != null
            ? builder.BuildServiceProvider(testMethod, services)
            : services.BuildServiceProvider();
    }

    private static async ValueTask StartAsync(
        ITestMethodContext testMethod,
        IReadOnlyList<Attribute> attributes,
        IServiceProvider provider
    )
    {
        foreach (var startupAttribute in attributes.OfType<ITestStartupAttribute>())
        {
            await startupAttribute.StartupAsync(testMethod, provider);
        }
    }
}

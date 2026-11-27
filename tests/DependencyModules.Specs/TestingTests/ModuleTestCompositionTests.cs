using System.Reflection;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.TestingTests;

/// <summary>
/// Drives ModuleTestComposition against a plain ServiceCollection, with no test framework around it.
/// </summary>
public partial class ModuleTestCompositionTests
{
    [VerifyTest]
    public async Task TheStepsRunInOrder()
    {
        var log = new Log();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        var builder = new BuilderAttribute(log, "build");

        await using var container = await Build(
            Context(
                nameof(Samples.Supplied),
                new EnvironmentAttribute(log, "Test"),
                new SetupAttribute(log, "setup", s => s.AddSingleton<SetupMarker>()),
                new FakeMockSupportAttribute(log),
                builder,
                new StartupAttribute(log)
            ),
            services: services,
            modules: [new ServiceModule(s => s.AddSingleton<ModuleMarker>(), log, "module")],
            overrides: [new TestServiceOverride(typeof(IThing), new Thing(), null)],
            currentTest: new FakeCurrentTest()
        );

        VerifyEquivalent(
            log.ToArray(),
            [
                "environment",
                "module",
                "mock support",
                "setup",
                "parameter value",
                "build",
                "startup",
            ]
        );

        var registered = builder.Registered;
        var source = registered.IndexOf(typeof(ITestContainerSource));
        var current = registered.IndexOf(typeof(ICurrentTestProvider));
        var environment = registered.IndexOf(typeof(IModuleEnvironment));
        var module = registered.IndexOf(typeof(ModuleMarker));
        var mock = registered.IndexOf(typeof(IThing));
        var setup = registered.IndexOf(typeof(SetupMarker));
        var parameter = registered.IndexOf(typeof(ParameterMarker));
        var overridden = registered.LastIndexOf(typeof(IThing));

        Verify(source >= 0 && source < current);
        Verify(current < environment);
        Verify(environment < module);
        Verify(module < mock);
        Verify(mock < setup);
        Verify(setup < parameter);
        Verify(parameter < overridden);
    }

    /// <summary>
    /// The list is reversed before LoadModules, as ModuleTestCase does.
    /// </summary>
    [VerifyTest]
    public async Task TheModulesAreLoadedReversed()
    {
        var thing = new ServiceModule(s => s.AddSingleton<IThing, Thing>());
        var other = new ServiceModule(s => s.AddSingleton<IThing, Other>());

        await using var container = await Build(
            Context(nameof(Samples.NoParameters)),
            modules: [thing, other]
        );

        Verify(container.Services.GetService<IThing>()?.GetType() == LoadedWinner(other, thing));
        Verify(LoadedWinner(other, thing) != LoadedWinner(thing, other));
    }

    [VerifyTest]
    public async Task TheNarrowestEnvironmentThatAnswersDecides()
    {
        var log = new Log();
        var module = new EnvironmentModule();

        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new EnvironmentAttribute(log, "Wide"),
                new EnvironmentAttribute(log, "Narrow"),
                new EnvironmentAttribute(log, null)
            ),
            modules: [module]
        );

        Verify(module.Observed?.EnvironmentName == "Narrow");
        Verify(container.Services.GetService<IModuleEnvironment>()?.EnvironmentName == "Narrow");
    }

    [VerifyTest]
    public async Task WithNoEnvironmentProviderNoEnvironmentIsRegistered()
    {
        var services = new ServiceCollection();

        await using var container = await Build(
            Context(nameof(Samples.NoParameters)),
            services: services
        );

        Verify(services.All(d => d.ServiceType != typeof(IModuleEnvironment)));
    }

    [VerifyTest]
    public async Task ATestExportBeatsAModule()
    {
        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) }
            ),
            modules: [new ServiceModule(s => s.AddSingleton<IThing, Other>())]
        );

        Verify(container.Services.GetService<IThing>() is Thing);
    }

    /// <summary>
    /// Mock support runs first whatever its position, so a named implementation beats a mock.
    /// </summary>
    [VerifyTest]
    public async Task MockSupportRunsBeforeTheOtherSetupAttributes()
    {
        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) },
                new FakeMockSupportAttribute(new Log())
            )
        );

        Verify(container.Services.GetService<IThing>() is Thing);
    }

    [VerifyTest]
    public async Task AnOverrideBeatsATestExport()
    {
        var instance = new Other();

        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) }
            ),
            overrides: [new TestServiceOverride(typeof(IThing), instance, null)]
        );

        Verify(ReferenceEquals(container.Services.GetService<IThing>(), instance));
    }

    [VerifyTest]
    public async Task AKeyedOverrideIsRegisteredUnderItsKey()
    {
        var instance = new Other();

        await using var container = await Build(
            Context(nameof(Samples.NoParameters)),
            overrides: [new TestServiceOverride(typeof(IThing), instance, "k")]
        );

        Verify(container.Services.GetService<IThing>() is null);
        Verify(ReferenceEquals(container.Resolve(typeof(IThing), "k"), instance));
    }

    /// <summary>
    /// [Mock] without a mock-support attribute throws in its SetupServiceCollection. With an override
    /// for the parameter, it does not run, and the parameter gets the override.
    /// </summary>
    [VerifyTest]
    public async Task AnOverriddenParameterRunsNoValueProvider()
    {
        var instance = new Thing();

        await using var container = await Build(
            Context(nameof(Samples.Mocked)),
            overrides: [new TestServiceOverride(typeof(IThing), instance, null)]
        );

        var arguments = await container.ResolveArgumentsAsync([]);
        var parameter = MethodOf(nameof(Samples.Mocked)).GetParameters()[0];

        Verify(ReferenceEquals(Single(arguments), instance));
        Verify(await container.GetParameterValueAsync(parameter) is null);
        Verify(ReferenceEquals(container.Resolve(parameter), instance));
    }

    [VerifyTest]
    public async Task AKeyedParameterIsOverriddenOnlyByItsKey()
    {
        var instance = new Thing();

        await using var container = await Build(
            Context(nameof(Samples.KeyedMock)),
            overrides: [new TestServiceOverride(typeof(IThing), instance, "k")]
        );

        Verify(ReferenceEquals(Single(await container.ResolveArgumentsAsync([])), instance));

        var exception = await ThrowsAsync<Exception>(async () =>
            await Build(
                Context(nameof(Samples.KeyedMock)),
                overrides: [new TestServiceOverride(typeof(IThing), instance, null)]
            )
        );

        Verify(exception.Message.Contains("Mock library not found"));
    }

    [VerifyTest]
    public async Task AParameterWithNoOverrideRunsItsValueProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Log());

        await using var container = await Build(
            Context(nameof(Samples.Supplied)),
            services: services
        );

        var parameter = MethodOf(nameof(Samples.Supplied)).GetParameters()[0];

        Verify((string?)Single(await container.ResolveArgumentsAsync([])) == "supplied");
        Verify((string?)await container.GetParameterValueAsync(parameter) == "supplied");
        Verify(services.Any(d => d.ServiceType == typeof(ParameterMarker)));
    }

    [VerifyTest]
    public async Task AnOverrideOfTheWrongTypeIsRejected()
    {
        var services = new ServiceCollection();

        var exception = await ThrowsAsync<ArgumentException>(async () =>
            await Build(
                Context(nameof(Samples.NoParameters)),
                services: services,
                overrides: [new TestServiceOverride(typeof(IThing), "text", null)]
            )
        );

        Verify(exception.ParamName == "overrides");
        Verify(services.Count == 0);
    }

    [VerifyTest]
    public async Task TheCurrentTestIsRegisteredWithoutChangingCurrentTestProvider()
    {
        var before = CurrentTest.Provider;
        var currentTest = new FakeCurrentTest();

        await using var container = await Build(
            Context(nameof(Samples.NoParameters)),
            currentTest: currentTest
        );

        Verify(ReferenceEquals(container.Services.GetService<ICurrentTestProvider>(), currentTest));
        Verify(ReferenceEquals(CurrentTest.Provider, before));
    }

    [VerifyTest]
    public async Task WithNoCurrentTestNoneIsRegistered()
    {
        await using var container = await Build(Context(nameof(Samples.NoParameters)));

        Verify(container.Services.GetService<ICurrentTestProvider>() is null);
    }

    [VerifyTest]
    public async Task TheNarrowestBuilderBuildsTheContainer()
    {
        var log = new Log();

        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new BuilderAttribute(log, "wide"),
                new BuilderAttribute(log, "narrow")
            )
        );

        VerifyEquivalent(log.ToArray(), ["narrow"]);
    }

    [VerifyTest]
    public async Task StartupGetsTheBuiltContainer()
    {
        IServiceProvider? started = null;

        await using var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new StartupAttribute(
                    new Log(),
                    provider =>
                    {
                        started = provider;
                        return Task.CompletedTask;
                    }
                )
            )
        );

        Verify(ReferenceEquals(started, container.Services));
    }

    /// <summary>
    /// The source builds with the same builder and runs the same startup, and the container tracks
    /// and disposes what it builds.
    /// </summary>
    [VerifyTest]
    public async Task TheContainerSourceBuildsLikeTheFirstContainer()
    {
        var log = new Log();
        var container = await Build(
            Context(
                nameof(Samples.NoParameters),
                new BuilderAttribute(log, "build"),
                new StartupAttribute(log)
            ),
            modules: [new ServiceModule(s => s.AddSingleton<Tracked>())]
        );

        var source = container.Services.GetRequiredService<ITestContainerSource>();
        var built = await source.CreateAsync();
        var first = container.Services.GetRequiredService<Tracked>();
        var second = built.GetRequiredService<Tracked>();

        VerifyEquivalent(log.ToArray(), ["build", "startup", "build", "startup"]);
        Verify(!ReferenceEquals(first, second));

        await container.DisposeAsync();

        Verify(first.Disposed);
        Verify(second.Disposed);
    }

    /// <summary>
    /// Every parameter is pinned across the containers that the source builds, as under xUnit.
    /// </summary>
    [VerifyTest]
    public async Task TheContainerSourceKeepsTheParameters()
    {
        await using var container = await Build(
            Context(nameof(Samples.TakesAStore)),
            modules: [new ServiceModule(s => s.AddSingleton<Store>())]
        );

        var built = await container
            .Services.GetRequiredService<ITestContainerSource>()
            .CreateAsync();

        Verify(
            ReferenceEquals(
                built.GetRequiredService<Store>(),
                container.Services.GetRequiredService<Store>()
            )
        );
    }

    [VerifyTest]
    public async Task AFailedStartupDisposesTheContainer()
    {
        Tracked? tracked = null;

        var exception = await ThrowsAsync<InvalidOperationException>(async () =>
            await Build(
                Context(
                    nameof(Samples.NoParameters),
                    new StartupAttribute(
                        new Log(),
                        provider =>
                        {
                            tracked = provider.GetRequiredService<Tracked>();
                            throw new InvalidOperationException("startup");
                        }
                    )
                ),
                modules: [new ServiceModule(s => s.AddSingleton<Tracked>())]
            )
        );

        Verify(exception.Message == "startup");
        Verify(tracked is { Disposed: true });
    }

    /// <summary>
    /// The exception of the failed step is the one rethrown, not the one of the disposal.
    /// </summary>
    [VerifyTest]
    public async Task AFailedDisposalDoesNotHideTheFailure()
    {
        var exception = await ThrowsAsync<InvalidOperationException>(async () =>
            await Build(
                Context(
                    nameof(Samples.NoParameters),
                    new ThrowingDisposalBuilderAttribute(),
                    new StartupAttribute(
                        new Log(),
                        _ => throw new InvalidOperationException("startup")
                    )
                )
            )
        );

        Verify(exception.Message == "startup");
    }

    [VerifyTest]
    public async Task AFailureBeforeTheBuildIsRethrown()
    {
        var exception = await ThrowsAsync<InvalidOperationException>(async () =>
            await Build(
                Context(
                    nameof(Samples.NoParameters),
                    new SetupAttribute(
                        new Log(),
                        "setup",
                        _ => throw new InvalidOperationException("setup")
                    )
                )
            )
        );

        Verify(exception.Message == "setup");
    }

    [VerifyTest]
    public async Task DisposeAsyncDisposesEveryContainerWhenOneThrows()
    {
        var container = await Build(
            Context(nameof(Samples.NoParameters), new ThrowingDisposalBuilderAttribute())
        );

        var built = await container
            .Services.GetRequiredService<ITestContainerSource>()
            .CreateAsync();

        var exception = await ThrowsAsync<AggregateException>(async () =>
            await container.DisposeAsync()
        );

        Verify(exception.InnerExceptions.Count == 2);
        Verify(((ThrowingDisposalProvider)built).DisposeCalled);
        Verify(((ThrowingDisposalProvider)container.Services).DisposeCalled);

        // Everything was disposed once. A second call has nothing left to dispose.
        await container.DisposeAsync();
    }

    [VerifyTest]
    public async Task OneDisposalFailureIsRethrownAsItIs()
    {
        var container = await Build(
            Context(nameof(Samples.NoParameters), new ThrowingDisposalBuilderAttribute())
        );

        var exception = await ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await container.DisposeAsync()
        );

        Verify(exception.Message == "dispose");
    }

    [VerifyTest]
    public async Task ADataRowSuppliesTheLeadingParameters()
    {
        await using var container = await Build(
            Context(nameof(Samples.Mixed)),
            modules: [new ServiceModule(s => s.AddSingleton<IThing, Thing>())]
        );

        var arguments = await container.ResolveArgumentsAsync([7]);

        Verify(arguments.Length == 3);
        Verify((int?)arguments[0] == 7);
        Verify(arguments[1] is Thing);
        Verify(ReferenceEquals(arguments[2], container.Services));
    }

    [VerifyTest]
    public async Task ResolveFindsServicesAndBuildsOnlyClasses()
    {
        var keyed = new Other();

        await using var container = await Build(
            Context(nameof(Samples.Resolvable)),
            modules:
            [
                new ServiceModule(s =>
                {
                    s.AddSingleton<IThing, Thing>();
                    s.AddKeyedSingleton<IThing>("k", keyed);
                }),
            ]
        );

        var parameters = MethodOf(nameof(Samples.Resolvable)).GetParameters();

        Verify(ReferenceEquals(container.Resolve(parameters[0]), container.Services));
        Verify(container.Resolve(parameters[1]) is Thing);
        Verify(ReferenceEquals(container.Resolve(parameters[2]), keyed));
        var constructed = (NeedsAValue)container.Resolve(parameters[3])!;

        Verify(constructed.Text == "text");
        Verify(constructed.Thing is Thing);
        Verify(container.Resolve(parameters[4]) is null);
        Verify(container.Resolve(parameters[5]) is null);

        Verify(container.Resolve(typeof(IThing), null) is Thing);
        Verify(ReferenceEquals(container.Resolve(typeof(IThing), "k"), keyed));
        Verify(container.Resolve(typeof(IThing), "missing") is null);
        Verify(container.Resolve(typeof(IOther), null) is null);
    }

    [VerifyTest]
    public async Task GetParameterValueAsyncRejectsAnotherMethodsParameter()
    {
        await using var container = await Build(Context(nameof(Samples.NoParameters)));

        var parameter = MethodOf(nameof(Samples.Mixed)).GetParameters()[0];

        await ThrowsAsync<ArgumentException>(async () =>
            await container.GetParameterValueAsync(parameter)
        );
    }

    [VerifyTest]
    public async Task AScopeContainerRegistersOnlyTheSharedTestExports()
    {
        await using var scope = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [new ServiceModule(s => s.AddSingleton<IThing, Other>())],
            [
                new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) },
                new TestExportAttribute(typeof(Store)) { Shared = true },
            ]
        );

        Verify(scope.Services.GetService<IThing>() is Other);
        Verify(scope.Services.GetService<Store>() is not null);
        Verify(scope.Services.GetService<ITestContainerSource>() is null);
    }

    [VerifyTest]
    public async Task AScopeContainerLoadsTheModulesReversed()
    {
        var thing = new ServiceModule(s => s.AddSingleton<IThing, Thing>());
        var other = new ServiceModule(s => s.AddSingleton<IThing, Other>());

        await using var scope = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [thing, other],
            []
        );

        Verify(scope.Services.GetService<IThing>()?.GetType() == LoadedWinner(other, thing));
    }

    [VerifyTest]
    public async Task AScopeContainerHasNoParameters()
    {
        await using var scope = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [],
            []
        );

        var parameter = MethodOf(nameof(Samples.Mixed)).GetParameters()[0];

        await ThrowsAsync<InvalidOperationException>(async () =>
            await scope.ResolveArgumentsAsync([])
        );
        await ThrowsAsync<InvalidOperationException>(async () =>
            await scope.GetParameterValueAsync(parameter)
        );
        Verify(scope.Resolve(parameter) is null);
    }

    /// <summary>
    /// A transient registration in the parent is still one instance for every child, and a child's
    /// own setup attribute for the type does not replace it.
    /// </summary>
    [VerifyTest]
    public async Task ChildrenShareTheParentsInstanceOfASharedParameter()
    {
        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [new ServiceModule(s => s.AddTransient<Store>())],
            []
        );

        var context = Context(nameof(Samples.SharedStore), new TestExportAttribute(typeof(Store)));
        IReadOnlyList<IDependencyModule> modules =
        [
            new ServiceModule(s => s.AddTransient<IThing, Thing>()),
        ];

        Store first;
        Store second;
        IThing firstThing;
        IThing secondThing;

        await using (var child = await Build(context, modules: modules, parent: parent))
        {
            var arguments = await child.ResolveArgumentsAsync([]);
            first = (Store)arguments[0]!;
            firstThing = (IThing)arguments[1]!;
        }

        await using (var child = await Build(context, modules: modules, parent: parent))
        {
            var arguments = await child.ResolveArgumentsAsync([]);
            second = (Store)arguments[0]!;
            secondThing = (IThing)arguments[1]!;
        }

        Verify(ReferenceEquals(first, second));
        Verify(!ReferenceEquals(firstThing, secondThing));

        // The children registered the instance, so they did not dispose it.
        Verify(!first.Disposed);
    }

    [VerifyTest]
    public async Task ChildrenShareTheParentsSharedTestExports()
    {
        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [],
            [
                new TestExportAttribute(typeof(IThing))
                {
                    Implementation = typeof(Thing),
                    Shared = true,
                },
                new TestExportAttribute(typeof(IOther)) { Implementation = typeof(Other) },
            ]
        );

        await using var child = await Build(
            Context(nameof(Samples.NoParameters)),
            modules: [new ServiceModule(s => s.AddSingleton<IOther, Other>())],
            parent: parent
        );
        await using var second = await Build(Context(nameof(Samples.NoParameters)), parent: parent);

        // The export is transient, so the parent itself returns a new instance on each call.
        Verify(child.Services.GetService<IThing>() is Thing);
        Verify(
            ReferenceEquals(
                child.Services.GetService<IThing>(),
                second.Services.GetService<IThing>()
            )
        );
        Verify(parent.Services.GetService<IOther>() is null);
        Verify(child.Services.GetService<IOther>() is Other);
    }

    [VerifyTest]
    public async Task AKeyedSharedParameterIsSharedUnderItsKey()
    {
        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [new ServiceModule(s => s.AddKeyedTransient<Store>("k"))],
            []
        );

        await using var first = await Build(
            Context(nameof(Samples.KeyedSharedStore)),
            parent: parent
        );
        await using var second = await Build(
            Context(nameof(Samples.KeyedSharedStore)),
            parent: parent
        );

        var store = Single(await first.ResolveArgumentsAsync([]));

        Verify(store is Store);
        Verify(ReferenceEquals(store, Single(await second.ResolveArgumentsAsync([]))));
        Verify(first.Services.GetService<Store>() is null);
    }

    [VerifyTest]
    public async Task AnIsolatedServiceIsNotTakenFromTheParent()
    {
        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [new ServiceModule(s => s.AddSingleton<Store>())],
            []
        );

        await using var child = await Build(
            Context(nameof(Samples.SharedStore), new IsolatesAttribute(typeof(Store))),
            modules: [new ServiceModule(s => s.AddSingleton<Store>())],
            parent: parent
        );

        Verify(
            !ReferenceEquals(
                child.Services.GetService<Store>(),
                parent.Services.GetService<Store>()
            )
        );
    }

    [VerifyTest]
    public async Task AnOverrideBeatsTheParentsInstance()
    {
        var instance = new Store();

        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [new ServiceModule(s => s.AddSingleton<Store>())],
            []
        );

        await using var child = await Build(
            Context(nameof(Samples.SharedStore)),
            overrides: [new TestServiceOverride(typeof(Store), instance, null)],
            parent: parent
        );

        Verify(ReferenceEquals(child.Services.GetService<Store>(), instance));
    }

    [VerifyTest]
    public async Task ASharedServiceTheParentDoesNotHaveIsLeftToTheChild()
    {
        await using var parent = await ModuleTestComposition.BuildScopeAsync(
            new ServiceCollection(),
            [],
            []
        );

        await using var child = await Build(
            Context(nameof(Samples.SharedStore)),
            modules: [new ServiceModule(s => s.AddSingleton<Store>())],
            parent: parent
        );

        Verify(child.Services.GetService<Store>() is not null);
    }

    [VerifyTest]
    public void TestMethodContextKeepsWhatItIsGiven()
    {
        var method = MethodOf(nameof(Samples.NoParameters));
        Attribute[] attributes = [new SharedAttribute()];

        var context = new TestMethodContext(method, attributes);

        Verify(ReferenceEquals(context.Method, method));
        Verify(ReferenceEquals(context.Attributes, attributes));
        Throws<ArgumentNullException>(() => new TestMethodContext(null!, attributes));
        Throws<ArgumentNullException>(() => new TestMethodContext(method, null!));
    }

    private static ValueTask<ModuleTestContainer> Build(
        TestMethodContext context,
        IServiceCollection? services = null,
        IReadOnlyList<IDependencyModule>? modules = null,
        IReadOnlyList<TestServiceOverride>? overrides = null,
        ICurrentTestProvider? currentTest = null,
        ModuleTestContainer? parent = null
    ) =>
        ModuleTestComposition.BuildAsync(
            services ?? new ServiceCollection(),
            modules ?? [],
            context,
            overrides ?? [],
            currentTest,
            parent
        );

    /// <summary>
    /// The type that wins when LoadModules is given these modules in this order.
    /// </summary>
    private static Type? LoadedWinner(params IDependencyModule[] modules)
    {
        var services = new ServiceCollection();
        DependencyRegistry<object>.LoadModules(services, modules);

        return services.BuildServiceProvider().GetService<IThing>()?.GetType();
    }

    private static TestMethodContext Context(string method, params Attribute[] attributes) =>
        new(MethodOf(method), attributes);

    private static MethodInfo MethodOf(string name) => typeof(Samples).GetMethod(name)!;

    private static class Samples
    {
        public static void NoParameters() { }

        public static void Supplied([Supplies("supplied")] string value) { }

        public static void Mocked([Mock] IThing thing) { }

        public static void KeyedMock([Mock] [FromKeyedServices("k")] IThing thing) { }

        public static void Mixed(int row, IThing thing, IServiceProvider provider) { }

        public static void TakesAStore(Store store) { }

        public static void SharedStore([Shared] Store store, IThing thing) { }

        public static void KeyedSharedStore([Shared] [FromKeyedServices("k")] Store store) { }

        public static void Resolvable(
            IServiceProvider provider,
            IThing thing,
            [FromKeyedServices("k")] IThing keyed,
            [InjectValues("text")] NeedsAValue value,
            IOther missing,
            [FromKeyedServices("missing")] IThing missingKeyed
        ) { }
    }

    private sealed class Log : List<string>;

    private interface IThing;

    private interface IOther;

    private sealed class Thing : IThing;

    private sealed class Other : IThing, IOther;

    private sealed class ModuleMarker;

    private sealed class SetupMarker;

    private sealed class ParameterMarker;

    private sealed class Store : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class Tracked : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class NeedsAValue(IThing thing, string text)
    {
        public IThing Thing { get; } = thing;

        public string Text { get; } = text;
    }

    private sealed class ServiceModule(
        Action<IServiceCollection> register,
        Log? log = null,
        string? name = null
    ) : IDependencyModule
    {
        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void InternalApplyServices(IServiceCollection serviceCollection)
        {
            if (name != null)
            {
                log?.Add(name);
            }

            register(serviceCollection);
        }
    }

    private sealed class EnvironmentModule
        : IDependencyModule,
            IEnvironmentServiceCollectionConfiguration
    {
        public IModuleEnvironment? Observed { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(
            IServiceCollection services,
            IModuleEnvironment environment
        ) => Observed = environment;
    }

    private sealed class StubEnvironment(string name) : IModuleEnvironment
    {
        public string EnvironmentName => name;

        public string? Value(string valueName) => null;
    }

    private sealed class FakeCurrentTest : ICurrentTestProvider
    {
        public object? Key => this;

        public string? DisplayName => "fake";

        public Assembly? Assembly => null;

        public bool TryWriteLine(string message) => false;
    }

    private sealed class EnvironmentAttribute(Log log, string? name)
        : Attribute,
            IModuleEnvironmentProvider
    {
        public IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod)
        {
            log.Add("environment");

            return name == null ? null : new StubEnvironment(name);
        }
    }

    private sealed class SetupAttribute(Log log, string name, Action<IServiceCollection> setup)
        : Attribute,
            ITestServiceSetupAttribute
    {
        public void SetupServiceCollection(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection
        )
        {
            log.Add(name);
            setup(serviceCollection);
        }
    }

    private sealed class FakeMockSupportAttribute(Log log)
        : Attribute,
            ITestServiceSetupAttribute,
            IMockSupportAttribute
    {
        public object ProvideMock(Type type) => throw new NotSupportedException();

        public void SetupServiceCollection(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection
        )
        {
            log.Add("mock support");
            serviceCollection.AddSingleton<IThing, Other>();
        }
    }

    private sealed class BuilderAttribute(Log log, string name)
        : Attribute,
            IServiceProviderBuilderAttribute
    {
        public List<Type> Registered { get; } = [];

        public IServiceProvider BuildServiceProvider(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection
        )
        {
            log.Add(name);
            Registered.Clear();
            Registered.AddRange(serviceCollection.Select(descriptor => descriptor.ServiceType));

            return serviceCollection.BuildServiceProvider();
        }
    }

    private sealed class ThrowingDisposalBuilderAttribute
        : Attribute,
            IServiceProviderBuilderAttribute
    {
        public IServiceProvider BuildServiceProvider(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection
        ) => new ThrowingDisposalProvider(serviceCollection.BuildServiceProvider());
    }

    private sealed class ThrowingDisposalProvider(ServiceProvider inner)
        : IServiceProvider,
            IDisposable
    {
        public bool DisposeCalled { get; private set; }

        public object? GetService(Type serviceType) => inner.GetService(serviceType);

        public void Dispose()
        {
            DisposeCalled = true;
            inner.Dispose();

            throw new InvalidOperationException("dispose");
        }
    }

    private sealed class StartupAttribute(Log log, Func<IServiceProvider, Task>? startup = null)
        : Attribute,
            ITestStartupAttribute
    {
        public Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider)
        {
            log.Add("startup");

            return startup?.Invoke(serviceProvider) ?? Task.CompletedTask;
        }
    }

    private sealed class IsolatesAttribute(Type service) : Attribute, ISharedTestRegistration
    {
        public IReadOnlyList<Type> IsolatedServices(MethodInfo testMethod) => [service];
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    private sealed class SuppliesAttribute(string value) : Attribute, ITestParameterValueProvider
    {
        public void SetupServiceCollection(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection,
            ParameterInfo parameter
        )
        {
            var log = (Log)
                serviceCollection
                    .Single(descriptor => descriptor.ServiceType == typeof(Log))
                    .ImplementationInstance!;

            log.Add("parameter " + parameter.Name);
            serviceCollection.AddSingleton(new ParameterMarker());
        }

        public Task<object?> GetParameterValueAsync(
            ITestMethodContext testMethod,
            IServiceProvider serviceProvider,
            ParameterInfo parameter
        ) => Task.FromResult<object?>(value);
    }
}

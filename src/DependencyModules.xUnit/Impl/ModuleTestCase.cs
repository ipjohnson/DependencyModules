using System.Reflection;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Represents a specialized implementation of <see cref="XunitTestCase"/>
/// tailored for module-based test scenarios within the xUnit framework.
/// </summary>
/// <remarks>
/// Self-executing, so that the container a test ran against is disposed when the test case has
/// run. The provider used to go into the case's <see cref="XunitTestCase.DisposalTracker"/>, and
/// xUnit disposes a test case only once every case in the assembly has run - in
/// <c>InProcessFrontController.FindAndRun</c>, after <c>Run</c> returns - so every container a run
/// built stayed alive, with every singleton in it, until the run ended. NUnit's
/// <c>ModuleTestCommand</c> has always disposed in a <c>finally</c> around the test; this is the
/// same lifetime for xUnit.
/// </remarks>
public class ModuleTestCase : XunitTestCase, ISelfExecutingXunitTestCase {

    /// <summary>
    /// One per container this case built: one for a plain test, one per row for a data-driven
    /// one. Runtime state only, never serialized with the case.
    /// </summary>
    private readonly List<IServiceProvider> _providers = [];

#pragma warning disable CS0618 // Type or member is obsolete
    /// <summary>
    /// Represents a specialized implementation of <see cref="XunitTestCase"/>
    /// designed to support module-based test scenarios within the xUnit testing framework.
    /// </summary>
    public ModuleTestCase() { }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Represents a specialized implementation of <see cref="XunitTestCase"/>
    /// tailored for module-based test scenarios within the xUnit framework.
    /// </summary>
    public ModuleTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        Type[]? skipExceptions = null,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        object?[]? testMethodArguments = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null) : base(
        // Named rather than positional throughout. XunitTestCase's constructor takes thirteen
        // parameters, eleven of them optional, and a version that inserts one mid-list rebinds
        // every argument after it — silently where the types happen to line up, and as a wall of
        // unrelated-looking conversion errors where they do not. Named arguments make an insertion
        // either invisible or a single precise error.
        testMethod: testMethod,
        testCaseDisplayName: testCaseDisplayName,
        uniqueID: uniqueID,
        @explicit: @explicit,
        skipExceptions: skipExceptions,
        skipReason: skipReason,
        skipType: skipType,
        skipUnless: skipUnless,
        skipWhen: skipWhen,
        traits: traits,
        testMethodArguments: testMethodArguments,
        sourceFilePath: sourceFilePath,
        sourceLineNumber: sourceLineNumber,
        timeout: timeout) { }

    /// <summary>
    /// Executes logic before the invocation of the test method associated with the current test case.
    /// Override this method to introduce custom pre-invocation behaviors specific to derived test case implementations.
    /// </summary>
    public override void PreInvoke() { }

    private record StartupValues(
        IServiceProvider ServiceProvider,
        TestParameterResolver Resolver);

    private async Task<StartupValues> SetupServiceCollection() {
        var serviceCollection = new ServiceCollection();

        var knownAttributes = TestMethod.Method.GetTestAttributes<Attribute>().ToArray();

        var context = new XunitTestMethodContext(TestMethod, knownAttributes);

        // One resolver per container. A data-driven test builds both again for every row.
        var resolver = new TestParameterResolver(context);

        SetupTestCaseInfo(serviceCollection, knownAttributes);

        SeedEnvironment(serviceCollection, knownAttributes);

        SetupModules(serviceCollection, knownAttributes);

        SetupServiceSetupAttributes(context, serviceCollection, knownAttributes);

        // Last, so a [Mock] on a parameter beats a [TestExport] naming the same service. A
        // parameter attribute is the narrowest thing a test can say and the only one that names a
        // single argument, so it decides for that argument - the class or assembly sets the default
        // and this is the one test opting out of it. A [Mock] stands aside where the mock library
        // registers the type itself, which is what keeps [Mock] IFoo and Mock<IFoo> one pair.
        resolver.SetupServiceCollection(serviceCollection);

        var provider = BuildServiceProvider(context, serviceCollection, knownAttributes);

        // Kept here rather than handed to DisposalTracker, which xUnit empties at the end of the
        // run; see the remarks on the class.
        _providers.Add(provider);

        foreach (var startupAttribute in knownAttributes.OfType<ITestStartupAttribute>()) {
            await startupAttribute.StartupAsync(context, provider);
        }

        return new StartupValues(provider, resolver);
    }

    private void SetupTestCaseInfo(ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        
        serviceCollection.AddSingleton<ITestCaseInfo>(provider => provider.GetRequiredService<TestCaseInfo>());
        serviceCollection.AddSingleton<TestCaseInfo>(_ => new TestCaseInfo(
            TestMethod,
            ArraySegment<object>.Empty, 
            knownAttributes
            ));
    }

    /// <remarks>
    /// Last rather than first: <paramref name="knownAttributes"/> is widest scope first — assembly,
    /// then declaring type, then the method — so the last one is the narrowest, and a builder on the
    /// method beats one on the class beats one on the assembly. Taking the first would have let an
    /// assembly-level builder silently win over the method that asked for a different container,
    /// which is the reverse of how every other attribute here resolves.
    /// </remarks>
    private IServiceProvider BuildServiceProvider(
        ITestMethodContext context, ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var serviceProviderBuilderAttribute =
            knownAttributes.OfType<IServiceProviderBuilderAttribute>().LastOrDefault();

        if (serviceProviderBuilderAttribute != null) {
            return serviceProviderBuilderAttribute.BuildServiceProvider(context, serviceCollection);
        }

        return serviceCollection.BuildServiceProvider();
    }

    /// <remarks>
    /// The whole pass runs <em>before</em> the parameter value providers, so a <c>[Mock]</c> on a
    /// parameter overrides a <c>[TestExport]</c> naming the same service. These attributes apply to
    /// a method, a class or an assembly; a parameter attribute names one argument, and that is the
    /// narrowest thing a test can say, so it is the one that decides.
    ///
    /// Mock support goes first within the pass, everything else keeping its declared order behind it.
    /// A mock is the stand-in a test falls back to, so naming a real implementation has to beat it —
    /// and has to beat it whether <c>[MoqSupport]</c> sits on the assembly, the class or the method,
    /// which relying on attribute order alone would not guarantee. A <c>Mock&lt;T&gt;</c> parameter
    /// goes through this pass rather than the parameter one, so it does <em>not</em> override a
    /// <c>[TestExport]</c>: asking for the mock object is not the same as declaring the service
    /// mocked, which is what <c>[Mock]</c> is for.
    /// </remarks>
    private void SetupServiceSetupAttributes(
        ITestMethodContext context, ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var setupAttributes = knownAttributes
            .OfType<ITestServiceSetupAttribute>()
            .OrderBy(attribute => attribute is IMockSupportAttribute ? 0 : 1);

        foreach (var setupAttribute in setupAttributes) {
            setupAttribute.SetupServiceCollection(context, serviceCollection);
        }
    }

    /// <summary>
    /// Registers the environment the test's attributes declare, ahead of the modules.
    /// </summary>
    /// <remarks>
    /// Before <see cref="SetupModules"/>, because module registrations are conditioned as they are
    /// applied: <c>LoadModules</c> answers <c>[IfEnvironment]</c> from the
    /// <see cref="IModuleEnvironment"/> already in the collection, or a process default when there
    /// is none. The service-setup pass runs after the modules by design - a test registration
    /// beats an application one - so an environment registered there arrived after every condition
    /// had been decided against the default. Widest scope first, so the narrowest attribute that
    /// answers decides, matching how every other attribute here resolves.
    /// </remarks>
    private void SeedEnvironment(IServiceCollection serviceCollection, Attribute[] knownAttributes) {
        IModuleEnvironment? environment = null;

        foreach (var provider in knownAttributes.OfType<IModuleEnvironmentProvider>()) {
            environment = provider.ProvideEnvironment(TestMethod.Method) ?? environment;
        }

        if (environment != null) {
            serviceCollection.Add(new ServiceDescriptor(typeof(IModuleEnvironment), environment));
        }
    }

    private void SetupModules(ServiceCollection serviceCollection, IEnumerable<Attribute> knownAttributes) {
        var modules = new List<IDependencyModule>();

        foreach (var loadModuleAttribute in knownAttributes.OfType<IDependencyModuleProvider>()) {

            var moduleTypes = loadModuleAttribute.GetModule();
            
            modules.Add(moduleTypes);
        }

        // The interface rather than ModuleTestAttribute, so this reads the same for any integration.
        var testAttribute = TestMethod.Method.GetTestAttribute<IModuleTestAttribute>();

        if (testAttribute != null) {
            var count = 0;
            foreach (var moduleType in testAttribute.ModuleTypes) {
                if (Activator.CreateInstance(moduleType, []) is IDependencyModule moduleInstance) {
                    modules.Insert(count++, moduleInstance);
                }
            }
        }

        modules.Reverse();
        
        DependencyRegistry<object>.LoadModules(serviceCollection, modules.ToArray());
    }

    /// <summary>
    /// Runs the case the way xUnit would have, and disposes every container it built once the
    /// run has returned - the tests passed, failed, were skipped or were cancelled alike.
    /// </summary>
    /// <remarks>
    /// <see cref="XunitRunnerHelper.RunXunitTestCase"/> is what the method runner calls for a
    /// case that does not execute itself: it creates the tests, turns a failure or a dynamic skip
    /// during creation into the case's result, and hands the tests to
    /// <see cref="XunitTestCaseRunner"/>. Wrapping that call is the whole of the difference.
    /// Disposal is per case, which for every test but a data-driven one is per test; the rows of
    /// a data-driven test share the case and are released together when the last has run.
    /// </remarks>
    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource) {
        try {
            return await XunitRunnerHelper.RunXunitTestCase(
                this, messageBus, cancellationTokenSource, aggregator, explicitOption, constructorArguments);
        }
        finally {
            await DisposeProviders();
        }
    }

    private async ValueTask DisposeProviders() {
        var providers = _providers.ToArray();

        _providers.Clear();

        foreach (var provider in providers) {
            switch (provider) {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    /// <remarks>
    ///     By default, this method returns a single <see cref="XunitTest" /> that is appropriate
    ///     for a one-to-one mapping between test and test case. Override this method to change the
    ///     tests that are associated with this test case.
    /// </remarks>
    /// <inheritdoc />
    public override async ValueTask<IReadOnlyCollection<IXunitTest>> CreateTests() {
        var dataAttributes =
            TestMethod.Method.GetTestAttributes<IDataAttribute>().ToArray();

        if (dataAttributes.Length == 0) {
            return await UnitTestWithNoDataAttributes();
        }

        SupplyReflectedType(dataAttributes);

        return await UnitTestFromDataAttributes(dataAttributes);
    }

    /// <summary>
    /// Tells every type-aware data attribute which type it was found on.
    /// </summary>
    /// <remarks>
    /// xUnit does this in <c>ExtensibilityPointFactory.GetMethodDataAttributes</c>, and
    /// <see cref="ITypeAwareDataAttribute"/>'s own documentation makes it the obligation of any
    /// framework that discovers data attributes some other way — which this one does, because it
    /// also sweeps the assembly and the declaring type for the attributes that compose the module.
    ///
    /// Skipping it was silent in the worst way. <c>[MemberData]</c> resolves its member against
    /// <see cref="ITypeAwareDataAttribute.MemberType"/>, and left null it returns an *empty* row
    /// collection rather than throwing — so every row vanished, the test case produced no tests,
    /// and the run reported a pass. Only <c>[MemberData(…, MemberType = typeof(X))]</c>, which
    /// needs no back-fill, kept working.
    ///
    /// Conditional, as the interface requires: an explicit MemberType is the author's answer and is
    /// never overwritten.
    /// </remarks>
    private void SupplyReflectedType(IDataAttribute[] dataAttributes) {
        var reflectedType = TestMethod.Method.ReflectedType;

        if (reflectedType == null) {
            return;
        }

        foreach (var typeAware in dataAttributes.OfType<ITypeAwareDataAttribute>()) {
            typeAware.MemberType ??= reflectedType;
        }
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestFromDataAttributes(IDataAttribute[] dataAttributes) {
        var unitTests = new List<IXunitTest>();

        foreach (var dataAttribute in dataAttributes) {
            var dataRowCollection =
                await dataAttribute.GetData(TestMethod.Method, DisposalTracker);

            foreach (var theoryDataRow in dataRowCollection) {
                var data = theoryDataRow.GetData();

                var startupValues = await SetupServiceCollection();

                unitTests.Add(
                    // testIndex is named for more than readability: XunitTest has a second
                    // nine-parameter constructor differing only at this position, taking a uniqueID
                    // string. Positionally the two are told apart by the argument's type alone.
                    new XunitTest(
                        testCase: this,
                        testMethod: TestMethod,
                        @explicit: Explicit,
                        skipReason: theoryDataRow.Skip ?? SkipReason,
                        // The row's own conditional-skip metadata takes precedence over the case's,
                        // matching how skipReason above already defers to it. These became required
                        // in xunit.v3 3.x; passing the case's values alone would have compiled and
                        // silently ignored [Theory]-style per-row skip conditions.
                        skipType: theoryDataRow.SkipType ?? SkipType,
                        skipUnless: theoryDataRow.SkipUnless ?? SkipUnless,
                        skipWhen: theoryDataRow.SkipWhen ?? SkipWhen,
                        testDisplayName: GetRowDisplayName(theoryDataRow, data),
                        testIndex: unitTests.Count,
                        traits: theoryDataRow.Traits?.ToReadOnlyTraits() ?? Traits.ToReadOnlyTraits(),
                        timeout: theoryDataRow.Timeout ?? Timeout,
                        testMethodArguments: await ResolveArguments(data, startupValues)
                    )
                );
            }
        }

        if (unitTests.Count == 0) {
            // Failing rather than returning nothing, which is what xUnit's own delay-enumerated
            // theory does for a theory without data. Returning an empty collection here is reported
            // as a pass, so a row source that stopped producing rows — for any reason, not only the
            // MemberType one above — took its coverage with it and left a green suite behind. The
            // NUnit half of this integration already refuses the equivalent case as NotRunnable.
            //
            // Exceptions thrown from CreateTests are caught and converted into a test case failure,
            // which is the documented way to surface this.
            throw new InvalidOperationException(
                $"No data was found for '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}'. " +
                $"It carries {DescribeAttributes(dataAttributes)}, and every one of them returned no rows. " +
                "A data-driven test with no rows runs nothing, so it is reported as a failure rather " +
                "than as a pass.");
        }

        return unitTests;
    }

    private static string DescribeAttributes(IDataAttribute[] dataAttributes) {
        var names = dataAttributes
            .Select(attribute => "[" + TrimAttributeSuffix(attribute.GetType().Name) + "]")
            .ToArray();

        return names.Length == 1 ? names[0] : string.Join(", ", names);
    }

    private static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;

    /// <summary>
    /// Names a data row after its own arguments, the way [Theory] does. Without this every row of
    /// a data-driven module test carries the same display name and the rows cannot be told apart
    /// in test explorers or result files.
    /// </summary>
    /// <remarks>
    /// Only the row's own data is used. The remaining parameters are resolved from the container
    /// at execution time, and naming a test after a service instance would be neither readable
    /// nor stable between runs.
    /// </remarks>
    private string GetRowDisplayName(Xunit.ITheoryDataRow theoryDataRow, object?[] data) {
        var baseDisplayName = theoryDataRow.TestDisplayName ?? TestCaseDisplayName;

        return TestMethod.GetDisplayName(
            baseDisplayName: baseDisplayName,
            // New in 3.x. The row may carry its own label, and xUnit folds it into the display name
            // for [Theory]; passing null would compile and quietly drop it for module tests.
            label: theoryDataRow.Label,
            testMethodArguments: data,
            methodGenericTypes: null);
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestWithNoDataAttributes() {
        var startupValues = await SetupServiceCollection();

        return [
            new XunitTest(
                testCase: this,
                testMethod: TestMethod,
                @explicit: Explicit,
                skipReason: SkipReason,
                skipType: SkipType,
                skipUnless: SkipUnless,
                skipWhen: SkipWhen,
                testDisplayName: TestCaseDisplayName,
                testIndex: 0,
                traits: Traits.ToReadOnlyTraits(),
                timeout: Timeout,
                testMethodArguments: await ResolveArguments([], startupValues)
            )
        ];
    }

    /// <remarks>
    /// The arguments are published on <see cref="TestCaseInfo"/> so a test can read what it was
    /// invoked with. That is xUnit's own object, which is why this is not part of the shared resolver.
    /// </remarks>
    private static async Task<object?[]> ResolveArguments(object?[] data, StartupValues startupValues) {
        var arguments = await startupValues.Resolver.ResolveArgumentsAsync(startupValues.ServiceProvider, data);

        startupValues.ServiceProvider.GetRequiredService<TestCaseInfo>().TestMethodArguments = arguments;

        return arguments;
    }
}
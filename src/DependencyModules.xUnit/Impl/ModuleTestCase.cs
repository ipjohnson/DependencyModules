using System.Reflection;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
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
public class ModuleTestCase : XunitTestCase {

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
        ITestMethodContext Context,
        IServiceProvider ServiceProvider,
        Dictionary<ParameterInfo, List<ITestParameterValueProvider>> KnownValues);

    private async Task<StartupValues> SetupServiceCollection() {
        var serviceCollection = new ServiceCollection();
        var knownValues = new Dictionary<ParameterInfo, List<ITestParameterValueProvider>>();

        var knownAttributes = TestMethod.Method.GetTestAttributes<Attribute>().ToArray();

        var context = new XunitTestMethodContext(TestMethod, knownAttributes);

        SetupTestCaseInfo(serviceCollection, knownAttributes);

        SetupModules(serviceCollection, knownAttributes);

        SetValueProviders(context, serviceCollection, knownValues);

        SetupServiceSetupAttributes(context, serviceCollection, knownAttributes);

        var provider = BuildServiceProvider(context, serviceCollection, knownAttributes);

        DisposalTracker.Add(provider);

        foreach (var startupAttribute in knownAttributes.OfType<ITestStartupAttribute>()) {
            await startupAttribute.StartupAsync(context, provider);
        }

        return new StartupValues(context, provider, knownValues);
    }

    private void SetupTestCaseInfo(ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        
        serviceCollection.AddSingleton<ITestCaseInfo>(provider => provider.GetRequiredService<TestCaseInfo>());
        serviceCollection.AddSingleton<TestCaseInfo>(_ => new TestCaseInfo(
            TestMethod,
            ArraySegment<object>.Empty, 
            knownAttributes
            ));
    }

    private IServiceProvider BuildServiceProvider(
        ITestMethodContext context, ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var serviceProviderBuilderAttribute =
            knownAttributes.OfType<IServiceProviderBuilderAttribute>().FirstOrDefault();

        if (serviceProviderBuilderAttribute != null) {
            return serviceProviderBuilderAttribute.BuildServiceProvider(context, serviceCollection);
        }

        return serviceCollection.BuildServiceProvider();
    }

    /// <remarks>
    /// The whole pass runs after the parameter value providers, so a <c>[TestExport]</c> overrides a
    /// <c>[Mock]</c> of the same service rather than the other way round.
    ///
    /// Mock support goes first within the pass, everything else keeping its declared order behind it.
    /// A mock is the stand-in a test falls back to, so naming a real implementation has to beat it —
    /// and has to beat it whether <c>[MoqSupport]</c> sits on the assembly, the class or the method,
    /// which relying on attribute order alone would not guarantee.
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

    private void SetValueProviders(
        ITestMethodContext context,
        ServiceCollection serviceCollection,
        Dictionary<ParameterInfo, List<ITestParameterValueProvider>> knownValues) {
        foreach (var parameterInfo in TestMethod.Method.GetParameters()) {
            var list = new List<ITestParameterValueProvider>();

            knownValues.Add(parameterInfo, list);

            foreach (var valueProvider in parameterInfo.GetCustomAttributes().OfType<ITestParameterValueProvider>()) {
                valueProvider.SetupServiceCollection(context, serviceCollection, parameterInfo);
                list.Add(valueProvider);
            }
        }
    }

    private void SetupModules(ServiceCollection serviceCollection, IEnumerable<Attribute> knownAttributes) {
        var modules = new List<IDependencyModule>();

        foreach (var loadModuleAttribute in knownAttributes.OfType<IDependencyModuleProvider>()) {

            var moduleTypes = loadModuleAttribute.GetModule();
            
            modules.Add(moduleTypes);
        }

        var testAttribute = TestMethod.Method.GetTestAttribute<ModuleTestAttribute>();

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
        return await UnitTestFromDataAttributes(dataAttributes);
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

        return unitTests;
    }

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

    private async Task<object?[]> ResolveArguments(object?[] data, StartupValues startupValues) {
        var parameters = new List<object?>(data);

        var testCaseInfo = startupValues.ServiceProvider.GetRequiredService<TestCaseInfo>();
        
        var parameterList = TestMethod.Method.GetParameters();

        for (var i = data.Length; i < parameterList.Length; i++) {
            var parameterInfo = parameterList[i];
            var attributes = parameterInfo.GetCustomAttributes().ToList();
            
            var value = await ResolveParameter(parameterInfo, startupValues);

            parameters.Add(value ?? ResolveArgumentFromProvider(parameterInfo, startupValues, attributes));
        }
        
        testCaseInfo.TestMethodArguments = parameters;
        
        return parameters.ToArray();
    }

    private async Task<object?> ResolveParameter(ParameterInfo parameterInfo, StartupValues startupValues) {
        object? value = null;

        if (parameterInfo.ParameterType == typeof(IServiceProvider)) {
            value = startupValues.ServiceProvider;
        }
        else {
            foreach (var valueProvider in startupValues.KnownValues[parameterInfo]) {
                value = await valueProvider.GetParameterValueAsync(
                    startupValues.Context, startupValues.ServiceProvider, parameterInfo);

                if (value != null) {
                    break;
                }
            }
        }

        return value;
    }

    private object? ResolveArgumentFromProvider(ParameterInfo parameterInfo, StartupValues startupValues, List<Attribute> attributes) {
        var keyedServicesAttribute = parameterInfo.GetCustomAttribute<FromKeyedServicesAttribute>();

        if (keyedServicesAttribute != null && startupValues.ServiceProvider is IKeyedServiceProvider keyedServiceProvider) {
            return keyedServiceProvider.GetKeyedService(parameterInfo.ParameterType, keyedServicesAttribute.Key);
        }
        
        var value = startupValues.ServiceProvider.GetService(parameterInfo.ParameterType);

        if (value != null) {
            return value;
        }

        return ConstructValueFromType(parameterInfo, startupValues, attributes);
    }

    private object? ConstructValueFromType(
        ParameterInfo parameterInfo,
        StartupValues startupValues,
        IReadOnlyList<Attribute> attributes) {
        object[] parameterValues = [];

        foreach (var attribute in attributes) {
            if (attribute is IInjectValueAttribute injectValueAttribute) {
                parameterValues = injectValueAttribute.ProvideValue(startupValues.ServiceProvider, parameterInfo);
            }
        }
        
        return ActivatorUtilities.CreateInstance(
            startupValues.ServiceProvider, parameterInfo.ParameterType, parameterValues);
    }
}
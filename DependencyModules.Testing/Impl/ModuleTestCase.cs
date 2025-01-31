using System.Reflection;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Internal;
using Xunit.v3;

namespace DependencyModules.Testing.Impl;

/// <summary>
///     xUnit test case implementation
/// </summary>
public class ModuleTestCase : XunitTestCase {
    private Dictionary<ParameterInfo, List<ITestParameterValueProvider>> _knownValues = new();
    private IServiceProvider? _serviceProvider;
    private List<ITestStartupAttribute> _startupAttributes = new();

#pragma warning disable CS0618 // Type or member is obsolete
    public ModuleTestCase() { }
#pragma warning restore CS0618 // Type or member is obsolete

    public ModuleTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        object?[]? testMethodArguments = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null) : base(
        testMethod,
        testCaseDisplayName,
        uniqueID,
        @explicit,
        skipReason,
        skipType,
        skipUnless,
        skipWhen,
        traits,
        testMethodArguments,
        sourceFilePath,
        sourceLineNumber,
        timeout) { }

    public override void PreInvoke() { }

    private void SetupServiceCollection() {
        var serviceCollection = new ServiceCollection();
        _knownValues = new Dictionary<ParameterInfo, List<ITestParameterValueProvider>>();
        _startupAttributes = new List<ITestStartupAttribute>();

        SetupModules(serviceCollection);

        SetValueProviders(serviceCollection);

        SetupStartupAttributes(serviceCollection);

        _serviceProvider = serviceCollection.BuildServiceProvider();
        
        DisposalTracker.Add(_serviceProvider);
    }

    private void SetupStartupAttributes(ServiceCollection serviceCollection) {
        foreach (var testStartupAttribute in TestMethod.Method.GetTestAttributes<ITestStartupAttribute>()) {
            testStartupAttribute.SetupServiceCollection(TestMethod, serviceCollection);
            _startupAttributes.Add(testStartupAttribute);
        }
    }

    private void SetValueProviders(ServiceCollection serviceCollection) {
        foreach (var parameterInfo in TestMethod.Method.GetParameters()) {
            var list = new List<ITestParameterValueProvider>();

            _knownValues.Add(parameterInfo, list);

            foreach (var valueProvider in parameterInfo.GetCustomAttributes().OfType<ITestParameterValueProvider>()) {
                valueProvider.SetupServiceCollection(TestMethod, serviceCollection, parameterInfo);
                list.Add(valueProvider);
            }
        }
    }

    private void SetupModules(ServiceCollection serviceCollection) {
        var modules = new List<IDependencyModule>();

        foreach (var loadModuleAttribute in
                 TestMethod.Method.GetTestAttributes<IDependencyModuleProvider>()) {

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

        ApplyAllModules(serviceCollection, modules.ToArray());
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

                SetupServiceCollection();

                unitTests.Add(
                    new XunitTest(
                        this,
                        TestMethod,
                        Explicit,
                        theoryDataRow.Skip ?? SkipReason,
                        TestCaseDisplayName,
                        unitTests.Count,
                        theoryDataRow.Traits?.ToReadOnly() ?? Traits.ToReadOnly(),
                        theoryDataRow.Timeout ?? Timeout,
                        await ResolveArguments(data)
                    )
                );
            }
        }

        return unitTests;
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestWithNoDataAttributes() {
        SetupServiceCollection();

        return [
            new XunitTest(
                this,
                TestMethod,
                Explicit,
                SkipReason,
                TestCaseDisplayName,
                0,
                Traits.ToReadOnly(),
                Timeout,
                await ResolveArguments([])
            )
        ];
    }

    private async Task<object?[]> ResolveArguments(object?[] data) {
        var parameters = new List<object?>(data);

        var parameterList = TestMethod.Method.GetParameters();

        for (var i = data.Length; i < parameterList.Length; i++) {
            var parameterInfo = parameterList[i];
            var value = await ResolveParameter(parameterInfo);

            parameters.Add(value ?? ResolveArgumentFromProvider(parameterInfo));
        }


        return parameters.ToArray();
    }

    private async Task<object?> ResolveParameter(ParameterInfo parameterInfo) {
        object? value = null;

        if (parameterInfo.ParameterType == typeof(IServiceProvider)) {
            value = _serviceProvider;
        }
        else {
            foreach (var valueProvider in _knownValues[parameterInfo]) {
                value = await valueProvider.GetParameterValueAsync(TestMethod, _serviceProvider!, parameterInfo);

                if (value != null) {
                    break;
                }
            }
        }

        return value;
    }

    private object? ResolveArgumentFromProvider(ParameterInfo parameterInfo) {
        var value = _serviceProvider!.GetService(parameterInfo.ParameterType);

        if (value != null) {
            return value;
        }

        return ConstructValueFromType(parameterInfo);
    }

    private object? ConstructValueFromType(ParameterInfo parameterInfo) {
        return ActivatorUtilities.CreateInstance(_serviceProvider!, parameterInfo.ParameterType);
    }

    private void ApplyAllModules(IServiceCollection serviceCollection, IDependencyModule[] dependencyModules) {
        foreach (var dependencyModule in GetAllModules(dependencyModules)) {
            dependencyModule.InternalApplyServices(serviceCollection);

            if (dependencyModule is IServiceCollectionConfiguration serviceCollectionConfigure) {
                serviceCollectionConfigure.ConfigureServices(serviceCollection);
            }
        }
    }

    private IEnumerable<IDependencyModule> GetAllModules(IDependencyModule[] dependencyModules) {
        var list = new List<IDependencyModule>();

        foreach (var dependencyModule in dependencyModules) {
            InternalGetModules(dependencyModule, list);
        }

        return list;
    }

    private void InternalGetModules(IDependencyModule dependencyModule, List<IDependencyModule> dependencyModules) {
        if (dependencyModules.Contains(dependencyModule)) {
            return;
        }

        dependencyModules.Insert(0, dependencyModule);

        foreach (var dependentModule in dependencyModule.InternalGetModules()) {
            if (dependentModule is IDependencyModuleProvider moduleProvider) {
                var dep = moduleProvider.GetModule();
                InternalGetModules(dep, dependencyModules);
            }
            else if (dependencyModule is IDependencyModule module) {
                InternalGetModules(module, dependencyModules);
            }
        }
    }
}
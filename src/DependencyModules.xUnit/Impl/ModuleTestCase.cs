using System.Reflection;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
using DependencyModules.xUnit.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Internal;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
///     xUnit test case implementation
/// </summary>
public class ModuleTestCase : XunitTestCase {

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

    private record StartupValues(
        IServiceProvider ServiceProvider, 
        Dictionary<ParameterInfo, List<ITestParameterValueProvider>> KnownValues);
    
    private StartupValues SetupServiceCollection() {
        var serviceCollection = new ServiceCollection();
        var knownValues = new Dictionary<ParameterInfo, List<ITestParameterValueProvider>>();
        
        var knownAttributes = TestMethod.Method.GetTestAttributes<Attribute>().ToArray();
        
        SetupTestCaseInfo(serviceCollection, knownAttributes);
        
        SetupModules(serviceCollection, knownAttributes);

        SetValueProviders(serviceCollection, knownValues);

        SetupStartupAttributes(serviceCollection, knownAttributes);

        var provider = BuildServiceProvider(serviceCollection, knownAttributes);
        
        DisposalTracker.Add(provider);
        
        return new StartupValues(provider, knownValues);
    }

    private void SetupTestCaseInfo(ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        
        serviceCollection.AddSingleton<ITestCaseInfo>(provider => provider.GetRequiredService<TestCaseInfo>());
        serviceCollection.AddSingleton<TestCaseInfo>(_ => new TestCaseInfo(
            TestMethod,
            ArraySegment<object>.Empty, 
            knownAttributes
            ));
    }

    private IServiceProvider BuildServiceProvider(ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var serviceProviderBuilderAttribute =
            knownAttributes.OfType<IServiceProviderBuilderAttribute>().FirstOrDefault();

        if (serviceProviderBuilderAttribute != null) {
            return serviceProviderBuilderAttribute.BuildServiceProvider(TestMethod, serviceCollection);
        }
        
        return serviceCollection.BuildServiceProvider();
    }

    private void SetupStartupAttributes(ServiceCollection serviceCollection, Attribute[] knownAttributes) {
        foreach (var testStartupAttribute in knownAttributes.OfType<ITestStartupAttribute>()) {
            testStartupAttribute.SetupServiceCollection(TestMethod, serviceCollection);
        }
    }

    private void SetValueProviders(ServiceCollection serviceCollection, Dictionary<ParameterInfo, List<ITestParameterValueProvider>> knownValues) {
        foreach (var parameterInfo in TestMethod.Method.GetParameters()) {
            var list = new List<ITestParameterValueProvider>();

            knownValues.Add(parameterInfo, list);

            foreach (var valueProvider in parameterInfo.GetCustomAttributes().OfType<ITestParameterValueProvider>()) {
                valueProvider.SetupServiceCollection(TestMethod, serviceCollection, parameterInfo);
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

                var startupValues = SetupServiceCollection();

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
                        await ResolveArguments(data, startupValues)
                    )
                );
            }
        }

        return unitTests;
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestWithNoDataAttributes() {
        var startupValues = SetupServiceCollection();

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
                await ResolveArguments([], startupValues)
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
                value = await valueProvider.GetParameterValueAsync(TestMethod, startupValues.ServiceProvider, parameterInfo);

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
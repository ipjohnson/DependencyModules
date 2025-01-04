using System.Reflection;
using System.Security.Authentication.ExtendedProtection;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Internal;
using Xunit.v3;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// xUnit test case implementation
/// </summary>
public class ModuleTestCase : XunitTestCase {
    private IServiceProvider? _serviceProvider;
    private Dictionary<ParameterInfo, List<ITestParameterValueProvider>> _knownValues = new();
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

    public override void PreInvoke() {

    }

    private void SetupServiceCollection() {
        var serviceCollection = new ServiceCollection();

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
            TestMethod.Method.GetTestAttributes<LoadModulesAttribute>()) {

            var moduleTypes = loadModuleAttribute.ModuleType;

            foreach (var moduleType in moduleTypes) {
                if (Activator.CreateInstance(moduleType, loadModuleAttribute.Parameters) is IDependencyModule moduleInstance) {
                    modules.Add(moduleInstance);
                }
            }
        }

        var testAttribute = TestMethod.Method.GetTestAttribute<ModuleTestAttribute>();

        if (testAttribute != null) {
            int count = 0;
            foreach (var moduleType in testAttribute.ModuleTypes) {
                if (Activator.CreateInstance(moduleType, []) is IDependencyModule moduleInstance) {
                    modules.Insert(count++, moduleInstance);
                }
            }
        }

        ApplyAllModules(serviceCollection, modules.ToArray());
    }

    /// <remarks>
    /// By default, this method returns a single <see cref="XunitTest"/> that is appropriate
    /// for a one-to-one mapping between test and test case. Override this method to change the
    /// tests that are associated with this test case.
    /// </remarks>
    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyCollection<IXunitTest>> CreateTests() {
        SetupServiceCollection();
        
        return [
            new XunitTest(
                this,
                TestMethod,
                Explicit,
                SkipReason,
                TestCaseDisplayName,
                testIndex: 0,
                Traits.ToReadOnly(),
                Timeout,
                await ResolveArguments()
            )
        ];
    }

    private async Task<object?[]> ResolveArguments() {
        var parameters = new List<object?>();

        foreach (var parameterInfo in TestMethod.Method.GetParameters()) {
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
            dependencyModule.ApplyServices(serviceCollection);
            
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

        foreach (var dependentModule in dependencyModule.GetDependentModules()) {
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
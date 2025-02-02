using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes;

/// <summary>
///     Mark test methods as ModuleTest
/// </summary>
[XunitTestCaseDiscoverer(typeof(ModuleTestDiscoverer))]
[AttributeUsage(AttributeTargets.Method)]
public class ModuleTestAttribute(params Type[] modules) : FactAttribute {
    public Type[] ModuleTypes {
        get;
    } = modules;
}
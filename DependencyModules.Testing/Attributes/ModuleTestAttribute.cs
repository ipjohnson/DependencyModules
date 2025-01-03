using DependencyModules.Testing.Impl;
using Xunit;
using Xunit.v3;

namespace DependencyModules.Testing.Attributes;

/// <summary>
/// Mark test methods as ModuleTest
/// </summary>
[XunitTestCaseDiscoverer(typeof(ModuleTestDiscoverer))]
[AttributeUsage(AttributeTargets.Method)]
public class ModuleTestAttribute(params Type[] modules) : FactAttribute {
    public Type[] ModuleTypes {
        get;
    } = modules;
}
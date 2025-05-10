using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.v3;

namespace DependencyModules.xUnit.Attributes;

/// <summary>
/// An attribute used for marking methods as module tests within xUnit-based testing.
/// This attribute extends the <see cref="FactAttribute"/> and integrates with custom module discovery
/// to facilitate dependency injection and modular test functionality.
/// </summary>
/// <remarks>
/// The <see cref="ModuleTestAttribute"/> supports passing specific module types to configure the
/// test case with required modules. Developers can use this attribute to integrate modular services,
/// apply dependency injection, or setup special configurations for tests.
/// </remarks>
/// <example>
/// This attribute is typically utilized on methods with dependencies or modules injected at runtime.
/// </example>
[XunitTestCaseDiscoverer(typeof(ModuleTestDiscoverer))]
[AttributeUsage(AttributeTargets.Method)]
public class ModuleTestAttribute(params Type[] modules) : FactAttribute {
    /// <summary>
    /// Gets an array of module types associated with the test method decorated with
    /// the <see cref="ModuleTestAttribute"/>.
    /// </summary>
    /// <remarks>
    /// This property holds the collection of module types that are passed as parameters
    /// when the <see cref="ModuleTestAttribute"/> is utilized. These types are used to
    /// configure and load dependency modules for the test case at runtime.
    /// </remarks>
    public Type[] ModuleTypes {
        get;
    } = modules;
}
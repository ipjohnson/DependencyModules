using System.Runtime.CompilerServices;
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
public class ModuleTestAttribute : FactAttribute {

    /// <summary>
    /// Marks a test method, taking no modules.
    /// </summary>
    /// <remarks>
    /// The source parameters are never passed by hand — the compiler fills them in with the
    /// location of the attribute usage, which is how a test reports where it is declared. They are
    /// what <c>FactAttribute</c> exists to capture for a derived attribute, and leaving them off
    /// makes every module test claim to live at this file instead of at the test.
    /// </remarks>
    public ModuleTestAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) =>
        ModuleTypes = [];

    /// <summary>
    /// Marks a test method and names one module to configure the test's container with.
    /// </summary>
    /// <remarks>
    /// Separate from the params overload rather than folded into it, because a params array has to
    /// be the last parameter and the caller-info parameters have to come after it — the two cannot
    /// coexist in one signature. Overload resolution prefers this normal form over expanding the
    /// params one, so the single-module case, which is the common one, keeps its source location.
    /// </remarks>
    public ModuleTestAttribute(
        Type module,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) =>
        ModuleTypes = [module];

    /// <summary>
    /// Marks a test method and names several modules to configure the test's container with.
    /// </summary>
    /// <remarks>
    /// Two or more modules land here, and this overload cannot capture a source location for the
    /// reason above: C# will not accept caller-info parameters after a params array. Such a test
    /// still runs and still reports correctly; only navigation from a test explorer back to the
    /// source is unavailable. Naming one module, or none, takes an overload that does capture it.
    /// </remarks>
    public ModuleTestAttribute(params Type[] modules) =>
        ModuleTypes = modules;

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
    }
}
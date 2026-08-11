namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Names the modules a test's container is built from.
/// </summary>
/// <remarks>
/// The attribute that marks a test method has to derive from whatever its framework demands —
/// <c>FactAttribute</c> for xUnit, <c>ITestBuilder</c> for NUnit — so it cannot be one shared type.
/// Which modules to load is not a framework question, though, and this is the part that is not:
/// an integration reads it rather than its own attribute, and the loading itself stays common.
///
/// Implemented by the integrations, not by test authors. A test names its modules through the
/// <c>[ModuleTest]</c> attribute of whichever framework it is written against.
/// </remarks>
public interface IModuleTestAttribute {

    /// <summary>
    /// The module types to load, in declaration order. Empty when a test names none.
    /// </summary>
    Type[] ModuleTypes {
        get;
    }
}

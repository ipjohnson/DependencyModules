using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.ParameterizedModuleTests;

public class DefaultedParameterModuleTests {

    /// <summary>
    /// A composition that names no parameters leaves the module's own initialiser in place.
    /// </summary>
    [ModuleTest(typeof(DefaultedParameterComposer))]
    public void AnUnnamedReferenceParameter_KeepsItsDefault(DefaultedParameterValues values) {
        Assert.Equal("default-label", values.Label);
    }

    /// <summary>
    /// The documented limit, pinned so it is a decision rather than a surprise: an attribute
    /// property of a value type cannot tell "not supplied" from the type's default, so a value-typed
    /// module parameter's initialiser does not survive composition by attribute.
    /// </summary>
    [ModuleTest(typeof(DefaultedParameterComposer))]
    public void AnUnnamedValueParameter_DoesNotKeepItsDefault(DefaultedParameterValues values) {
        Assert.Equal(0, values.Size);
    }

    [ModuleTest(typeof(NamedParameterComposer))]
    public void NamedParameters_AreCarriedAcross(DefaultedParameterValues values) {
        Assert.Equal("named-label", values.Label);
        Assert.Equal(7, values.Size);
    }
}

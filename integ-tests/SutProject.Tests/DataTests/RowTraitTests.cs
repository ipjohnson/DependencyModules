using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DataTests;

[Trait("Area", "Checkout")]
public class RowTraitTests
{
    [ModuleTest]
    [Trait("Category", "Fast")]
    [InlineData("first")]
    [InlineData("second", Traits = new[] { "Row", "Second" })]
    [SutModule]
    public void ARowKeepsTheTraitsOfTheClassAndTheMethod(string value, IDependencyOne one)
    {
        var traits = TestContext.Current.Test!.Traits;

        Assert.Equal(["Checkout"], traits["Area"]);
        Assert.Equal(["Fast"], traits["Category"]);
        Assert.Equal(value == "second", traits.ContainsKey("Row"));
        Assert.NotNull(one);
    }
}

using System.Reflection;
using DependencyModules.xUnit.Impl;
using Xunit;

namespace DependencyModules.Tests.xUnitTests;

/// <summary>
/// AttributeUtility backs the documented "test attributes can be applied at the assembly, class,
/// and test method level" behaviour, so the lookup order across those levels is the contract.
/// </summary>
public class AttributeUtilityTests {

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    private class MarkerAttribute(string source) : Attribute {
        public string Source { get; } = source;
    }

    [AttributeUsage(AttributeTargets.All)]
    private class UnusedAttribute : Attribute;

    [Marker("class")]
    private class WithClassAttribute {
        [Marker("method")]
        public void MethodWithItsOwn(string plain) { }

        public void MethodWithout(string plain) { }

        public void MethodWithParameterAttribute([Marker("parameter")] string annotated) { }
    }

    private static MethodInfo Method(string name) =>
        typeof(WithClassAttribute).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!;

    private static ParameterInfo Parameter(string methodName) => Method(methodName).GetParameters()[0];

    [Fact]
    public void GetTestAttribute_FindsAnAttributeOnTheMethod() {
        var attribute = Method(nameof(WithClassAttribute.MethodWithItsOwn)).GetTestAttribute<MarkerAttribute>();

        Assert.Equal("method", attribute?.Source);
    }

    [Fact]
    public void GetTestAttribute_FallsBackToTheDeclaringType() {
        var attribute = Method(nameof(WithClassAttribute.MethodWithout)).GetTestAttribute<MarkerAttribute>();

        Assert.Equal("class", attribute?.Source);
    }

    [Fact]
    public void GetTestAttribute_ReturnsNullWhenNothingMatches() {
        Assert.Null(Method(nameof(WithClassAttribute.MethodWithout)).GetTestAttribute<UnusedAttribute>());
    }

    [Fact]
    public void GetTestAttribute_OnAParameter_PrefersTheParameterAttribute() {
        var attribute = Parameter(nameof(WithClassAttribute.MethodWithParameterAttribute))
            .GetTestAttribute<MarkerAttribute>();

        Assert.Equal("parameter", attribute?.Source);
    }

    [Fact]
    public void GetTestAttribute_OnAnUnannotatedParameter_FallsBackToTheMethod() {
        var attribute = Parameter(nameof(WithClassAttribute.MethodWithItsOwn)).GetTestAttribute<MarkerAttribute>();

        Assert.Equal("method", attribute?.Source);
    }

    [Fact]
    public void GetTestAttribute_OnAnUnannotatedParameter_FallsBackToTheDeclaringType() {
        var attribute = Parameter(nameof(WithClassAttribute.MethodWithout)).GetTestAttribute<MarkerAttribute>();

        Assert.Equal("class", attribute?.Source);
    }

    [Fact]
    public void GetTestAttribute_OnAParameter_ReturnsNullWhenNothingMatches() {
        Assert.Null(Parameter(nameof(WithClassAttribute.MethodWithout)).GetTestAttribute<UnusedAttribute>());
    }

    [Fact]
    public void GetTestAttributes_AccumulatesTypeThenMethod() {
        var sources = Method(nameof(WithClassAttribute.MethodWithItsOwn))
            .GetTestAttributes<MarkerAttribute>()
            .Select(attribute => attribute.Source)
            .ToArray();

        Assert.Equal(["class", "method"], sources);
    }

    [Fact]
    public void GetTestAttributes_ReturnsJustTheTypeAttributeWhenTheMethodHasNone() {
        var sources = Method(nameof(WithClassAttribute.MethodWithout))
            .GetTestAttributes<MarkerAttribute>()
            .Select(attribute => attribute.Source)
            .ToArray();

        Assert.Equal(["class"], sources);
    }

    [Fact]
    public void GetTestAttributes_ReturnsEmptyWhenNothingMatches() {
        Assert.Empty(Method(nameof(WithClassAttribute.MethodWithout)).GetTestAttributes<UnusedAttribute>());
    }

    [Fact]
    public void GetTestAttributes_OnAParameter_AccumulatesTypeMethodThenParameter() {
        var sources = Parameter(nameof(WithClassAttribute.MethodWithParameterAttribute))
            .GetTestAttributes<MarkerAttribute>()
            .Select(attribute => attribute.Source)
            .ToArray();

        Assert.Equal(["class", "parameter"], sources);
    }

    [Fact]
    public void GetTestAttributes_OnAParameter_ReturnsEmptyWhenNothingMatches() {
        Assert.Empty(Parameter(nameof(WithClassAttribute.MethodWithout)).GetTestAttributes<UnusedAttribute>());
    }
}

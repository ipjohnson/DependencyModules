using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator's model records feed Roslyn's incremental cache, so two structurally identical
/// models built on consecutive runs must compare equal. A positional record compares
/// IReadOnlyList members by reference, which silently disables caching — these tests pin the
/// structural semantics that replaced it.
/// </summary>
public class ModelEqualityTests {

    private static readonly ITypeDefinition SomeType = TypeDefinition.Get("Ns", "SomeType");
    private static readonly ITypeDefinition OtherType = TypeDefinition.Get("Ns", "OtherType");

    [Fact]
    public void AttributeModel_WithSeparateButEqualLists_IsEqual() {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "value")]);
        var second = Attribute(arguments: [new AttributeArgumentValue("key", "value")]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void AttributeModel_WithEmptyLists_IsEqual() {
        Assert.Equal(Attribute(), Attribute());
    }

    [Fact]
    public void AttributeModel_WithDifferentArguments_IsNotEqual() {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "one")]);
        var second = Attribute(arguments: [new AttributeArgumentValue("key", "two")]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeModel_WithDifferentArgumentCounts_IsNotEqual() {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "one")]);
        var second = Attribute();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeModel_WithDifferentProperties_IsNotEqual() {
        var first = Attribute(properties: [new AttributeArgumentValue("P", 1)]);
        var second = Attribute(properties: [new AttributeArgumentValue("P", 2)]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeModel_WithDifferentImplementedInterfaces_IsNotEqual() {
        var first = Attribute(interfaces: [SomeType]);
        var second = Attribute(interfaces: [OtherType]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeModel_WithDifferentType_IsNotEqual() {
        var first = new AttributeModel(SomeType, [], [], []);
        var second = new AttributeModel(OtherType, [], [], []);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeArgumentValue_WithEqualArrayValues_IsEqual() {
        var first = new AttributeArgumentValue("names", new[] { "a", "b" });
        var second = new AttributeArgumentValue("names", new[] { "a", "b" });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void AttributeArgumentValue_WithDifferentArrayValues_IsNotEqual() {
        var first = new AttributeArgumentValue("names", new[] { "a", "b" });
        var second = new AttributeArgumentValue("names", new[] { "a", "c" });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeArgumentValue_WithDifferentArrayLengths_IsNotEqual() {
        var first = new AttributeArgumentValue("names", new[] { "a" });
        var second = new AttributeArgumentValue("names", new[] { "a", "b" });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttributeArgumentValue_WithDifferentNames_IsNotEqual() {
        Assert.NotEqual(
            new AttributeArgumentValue("one", "value"),
            new AttributeArgumentValue("two", "value"));
    }

    [Fact]
    public void AttributeArgumentValue_WithNullValues_IsEqual() {
        Assert.Equal(
            new AttributeArgumentValue("key", null),
            new AttributeArgumentValue("key", null));
    }

    [Fact]
    public void AttributeArgumentValue_NullVersusValue_IsNotEqual() {
        Assert.NotEqual(
            new AttributeArgumentValue("key", null),
            new AttributeArgumentValue("key", "value"));
    }

    /// <summary>
    /// A string is IEnumerable; it must not be compared character by character against a
    /// char collection.
    /// </summary>
    [Fact]
    public void AttributeArgumentValue_StringVersusCharArray_IsNotEqual() {
        Assert.NotEqual(
            new AttributeArgumentValue("key", "ab"),
            new AttributeArgumentValue("key", new[] { 'a', 'b' }));
    }

    [Fact]
    public void ParameterInfoModel_WithSeparateButEqualAttributes_IsEqual() {
        var first = new ParameterInfoModel("name", SomeType, null, [Attribute()]);
        var second = new ParameterInfoModel("name", SomeType, null, [Attribute()]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ParameterInfoModel_WithDifferentNames_IsNotEqual() {
        Assert.NotEqual(
            new ParameterInfoModel("one", SomeType, null, []),
            new ParameterInfoModel("two", SomeType, null, []));
    }

    [Fact]
    public void ParameterInfoModel_WithDifferentTypes_IsNotEqual() {
        Assert.NotEqual(
            new ParameterInfoModel("name", SomeType, null, []),
            new ParameterInfoModel("name", OtherType, null, []));
    }

    [Fact]
    public void ParameterInfoModel_WithDifferentDefaultValues_IsNotEqual() {
        Assert.NotEqual(
            new ParameterInfoModel("name", SomeType, 1, []),
            new ParameterInfoModel("name", SomeType, 2, []));
    }

    [Fact]
    public void ConstructorInfoModel_WithSeparateButEqualParameters_IsEqual() {
        var first = new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]);
        var second = new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ConstructorInfoModel_WithDifferentParameters_IsNotEqual() {
        Assert.NotEqual(
            new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]),
            new ConstructorInfoModel([new ParameterInfoModel("b", SomeType, null, [])]));
    }

    [Fact]
    public void ServiceFactoryModel_WithSeparateButEqualParameters_IsEqual() {
        var first = new ServiceFactoryModel(SomeType, "Create", [new ParameterInfoModel("a", SomeType, null, [])]);
        var second = new ServiceFactoryModel(SomeType, "Create", [new ParameterInfoModel("a", SomeType, null, [])]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ServiceFactoryModel_WithDifferentMethodNames_IsNotEqual() {
        Assert.NotEqual(
            new ServiceFactoryModel(SomeType, "Create", []),
            new ServiceFactoryModel(SomeType, "Build", []));
    }

    [Fact]
    public void ServiceFactoryModel_WithDifferentDeclaringTypes_IsNotEqual() {
        Assert.NotEqual(
            new ServiceFactoryModel(SomeType, "Create", []),
            new ServiceFactoryModel(OtherType, "Create", []));
    }

    private static AttributeModel Attribute(
        IReadOnlyList<AttributeArgumentValue>? arguments = null,
        IReadOnlyList<AttributeArgumentValue>? properties = null,
        IReadOnlyList<ITypeDefinition>? interfaces = null) =>
        new(SomeType,
            arguments ?? new List<AttributeArgumentValue>(),
            properties ?? new List<AttributeArgumentValue>(),
            interfaces ?? new List<ITypeDefinition>());
}

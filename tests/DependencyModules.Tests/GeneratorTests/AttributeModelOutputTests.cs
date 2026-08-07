using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// AttributeModel turns the values captured from a source attribute back into C# for the generated
/// module attribute. Each supported value shape has to round-trip into code that compiles.
/// </summary>
public class AttributeModelOutputTests {

    private static readonly ITypeDefinition SomeType = TypeDefinition.Get("Ns", "SomeType");

    [Fact]
    public void GetArguments_WithNoArguments_ReturnsNothing() {
        Assert.Empty(Attribute().GetArguments());
    }

    [Fact]
    public void GetArguments_QuotesStringValues() {
        var rendered = Render(Attribute(arguments: [new AttributeArgumentValue("name", "hello")]).GetArguments());

        Assert.Contains("\"hello\"", rendered);
    }

    [Fact]
    public void GetArguments_WritesTypeValuesAsTypeof() {
        var rendered = Render(Attribute(arguments: [new AttributeArgumentValue("type", SomeType)]).GetArguments());

        Assert.Contains("typeof(", rendered);
        Assert.Contains("SomeType", rendered);
    }

    [Fact]
    public void GetArguments_WritesPrimitiveValuesVerbatim() {
        var rendered = Render(Attribute(arguments: [new AttributeArgumentValue("count", 42)]).GetArguments());

        Assert.Contains("42", rendered);
    }

    [Fact]
    public void GetArguments_WritesBooleanValues() {
        var rendered = Render(Attribute(arguments: [new AttributeArgumentValue("flag", true)]).GetArguments());

        Assert.Contains("True", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetArguments_SkipsNullValues() {
        Assert.Empty(Attribute(arguments: [new AttributeArgumentValue("nothing", null)]).GetArguments());
    }

    [Fact]
    public void GetArguments_WritesStringArraysAsACollection() {
        var rendered = Render(Attribute(
            arguments: [new AttributeArgumentValue("names", new[] { "a", "b" })]).GetArguments());

        Assert.StartsWith("[", rendered);
        Assert.EndsWith("]", rendered);
        Assert.Contains("\"a\"", rendered);
        Assert.Contains("\"b\"", rendered);
        Assert.Contains(",", rendered);
    }

    [Fact]
    public void GetArguments_PassesThroughOutputComponents() {
        var component = CodeOutputComponent.Get("SomeExpression");

        var rendered = Render(Attribute(arguments: [new AttributeArgumentValue("value", component)]).GetArguments());

        Assert.Contains("SomeExpression", rendered);
    }

    [Fact]
    public void GetArguments_PreservesArgumentOrder() {
        var rendered = Render(Attribute(arguments: [
            new AttributeArgumentValue("first", "one"),
            new AttributeArgumentValue("second", "two")
        ]).GetArguments());

        Assert.True(rendered.IndexOf("one", StringComparison.Ordinal) < rendered.IndexOf("two", StringComparison.Ordinal),
            $"Arguments came out in the wrong order: {rendered}");
    }

    [Fact]
    public void PropertyValues_WithNoProperties_ReturnsNothing() {
        Assert.Empty(Attribute().PropertyValues());
    }

    [Fact]
    public void PropertyValues_WritesNamedAssignments() {
        var rendered = Render(Attribute(
            properties: [new AttributeArgumentValue("Name", "value")]).PropertyValues());

        Assert.Contains("Name", rendered);
        Assert.Contains("=", rendered);
        Assert.Contains("\"value\"", rendered);
    }

    /// <summary>
    /// Property values arrive from the syntax tree already quoted; re-quoting would emit
    /// a doubly-quoted literal.
    /// </summary>
    [Fact]
    public void PropertyValues_DoesNotDoubleQuoteAnAlreadyQuotedString() {
        var rendered = Render(Attribute(
            properties: [new AttributeArgumentValue("Name", "\"value\"")]).PropertyValues());

        Assert.DoesNotContain("\"\"", rendered);
        Assert.Contains("\"value\"", rendered);
    }

    [Fact]
    public void PropertyValues_WritesTypeValuesAsTypeof() {
        var rendered = Render(Attribute(
            properties: [new AttributeArgumentValue("As", SomeType)]).PropertyValues());

        Assert.Contains("typeof(", rendered);
    }

    [Fact]
    public void PropertyValues_SkipsNullValues() {
        Assert.Empty(Attribute(properties: [new AttributeArgumentValue("Name", null)]).PropertyValues());
    }

    [Fact]
    public void CollectionSyntax_WithNoItems_WritesEmptyBrackets() {
        Assert.Equal("[]", Render(new CollectionSyntaxDeclaration()));
    }

    [Fact]
    public void CollectionSyntax_QuotesStringItems() {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add("value");

        Assert.Equal("[\"value\"]", Render(collection));
    }

    [Fact]
    public void CollectionSyntax_SeparatesItemsWithCommas() {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add("one");
        collection.Add("two");

        Assert.Equal("[\"one\", \"two\"]", Render(collection));
    }

    [Fact]
    public void CollectionSyntax_WritesNonStringItemsVerbatim() {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add(1);
        collection.Add(2);

        Assert.Equal("[1, 2]", Render(collection));
    }

    [Fact]
    public void CollectionSyntax_WithTheSameItems_IsEqual() {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        var second = new CollectionSyntaxDeclaration();
        second.Add("a");

        Assert.True(first.Equals(second));
    }

    [Fact]
    public void CollectionSyntax_WithDifferentItems_IsNotEqual() {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        var second = new CollectionSyntaxDeclaration();
        second.Add("b");

        Assert.False(first.Equals(second));
    }

    [Fact]
    public void CollectionSyntax_WithDifferentCounts_IsNotEqual() {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        Assert.False(first.Equals(new CollectionSyntaxDeclaration()));
    }

    [Fact]
    public void CollectionSyntax_IsNotEqualToOtherTypes() {
        Assert.False(new CollectionSyntaxDeclaration().Equals("not a collection"));
    }

    private static AttributeModel Attribute(
        IReadOnlyList<AttributeArgumentValue>? arguments = null,
        IReadOnlyList<AttributeArgumentValue>? properties = null) =>
        new(SomeType, arguments ?? [], properties ?? [], []);

    private static string Render(IEnumerable<IOutputComponent> components) {
        var context = new OutputContext(new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global });

        foreach (var component in components) {
            component.WriteOutput(context);
        }

        return context.Output();
    }

    private static string Render(IOutputComponent component) => Render([component]);
}

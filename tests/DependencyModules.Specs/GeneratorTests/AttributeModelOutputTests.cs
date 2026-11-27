using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// AttributeModel turns the values captured from a source attribute back into C# for the generated
/// module attribute. Each supported value shape has to round-trip into code that compiles.
/// </summary>
public partial class AttributeModelOutputTests
{
    private static readonly ITypeDefinition SomeType = TypeDefinition.Get("Ns", "SomeType");

    [VerifyTest]
    public void GetArguments_WithNoArguments_ReturnsNothing()
    {
        Verify(!Attribute().GetArguments().Any());
    }

    [VerifyTest]
    public void GetArguments_QuotesStringValues()
    {
        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("name", "hello")]).GetArguments()
        );

        Verify(rendered.Contains("\"hello\""));
    }

    [VerifyTest]
    public void GetArguments_WritesTypeValuesAsTypeof()
    {
        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("type", SomeType)]).GetArguments()
        );

        Verify(rendered.Contains("typeof("));
        Verify(rendered.Contains("SomeType"));
    }

    [VerifyTest]
    public void GetArguments_WritesPrimitiveValuesVerbatim()
    {
        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("count", 42)]).GetArguments()
        );

        Verify(rendered.Contains("42"));
    }

    [VerifyTest]
    public void GetArguments_WritesBooleanValues()
    {
        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("flag", true)]).GetArguments()
        );

        Verify(rendered.Contains("True", StringComparison.OrdinalIgnoreCase));
    }

    [VerifyTest]
    public void GetArguments_SkipsNullValues()
    {
        Verify(
            !Attribute(arguments: [new AttributeArgumentValue("nothing", null)])
                .GetArguments()
                .Any()
        );
    }

    [VerifyTest]
    public void GetArguments_WritesStringArraysAsACollection()
    {
        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("names", new[] { "a", "b" })])
                .GetArguments()
        );

        Verify(rendered.StartsWith("["));
        Verify(rendered.EndsWith("]"));
        Verify(rendered.Contains("\"a\""));
        Verify(rendered.Contains("\"b\""));
        Verify(rendered.Contains(","));
    }

    [VerifyTest]
    public void GetArguments_PassesThroughOutputComponents()
    {
        var component = CodeOutputComponent.Get("SomeExpression");

        var rendered = Render(
            Attribute(arguments: [new AttributeArgumentValue("value", component)]).GetArguments()
        );

        Verify(rendered.Contains("SomeExpression"));
    }

    [VerifyTest]
    public void GetArguments_PreservesArgumentOrder()
    {
        var rendered = Render(
            Attribute(
                    arguments:
                    [
                        new AttributeArgumentValue("first", "one"),
                        new AttributeArgumentValue("second", "two"),
                    ]
                )
                .GetArguments()
        );

        Verify(
            rendered.IndexOf("one", StringComparison.Ordinal)
                < rendered.IndexOf("two", StringComparison.Ordinal),
            $"Arguments came out in the wrong order: {rendered}"
        );
    }

    [VerifyTest]
    public void PropertyValues_WithNoProperties_ReturnsNothing()
    {
        Verify(!Attribute().PropertyValues().Any());
    }

    [VerifyTest]
    public void PropertyValues_WritesNamedAssignments()
    {
        var rendered = Render(
            Attribute(properties: [new AttributeArgumentValue("Name", "value")]).PropertyValues()
        );

        Verify(rendered.Contains("Name"));
        Verify(rendered.Contains("="));
        Verify(rendered.Contains("\"value\""));
    }

    /// <summary>
    /// Property values arrive from the syntax tree already quoted; re-quoting would emit
    /// a doubly-quoted literal.
    /// </summary>
    [VerifyTest]
    public void PropertyValues_DoesNotDoubleQuoteAnAlreadyQuotedString()
    {
        var rendered = Render(
            Attribute(properties: [new AttributeArgumentValue("Name", "\"value\"")])
                .PropertyValues()
        );

        Verify(!rendered.Contains("\"\""));
        Verify(rendered.Contains("\"value\""));
    }

    [VerifyTest]
    public void PropertyValues_WritesTypeValuesAsTypeof()
    {
        var rendered = Render(
            Attribute(properties: [new AttributeArgumentValue("As", SomeType)]).PropertyValues()
        );

        Verify(rendered.Contains("typeof("));
    }

    [VerifyTest]
    public void PropertyValues_SkipsNullValues()
    {
        Verify(
            !Attribute(properties: [new AttributeArgumentValue("Name", null)])
                .PropertyValues()
                .Any()
        );
    }

    [VerifyTest]
    public void CollectionSyntax_WithNoItems_WritesEmptyBrackets()
    {
        Verify(Render(new CollectionSyntaxDeclaration()) == "[]");
    }

    [VerifyTest]
    public void CollectionSyntax_QuotesStringItems()
    {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add("value");

        Verify(Render(collection) == "[\"value\"]");
    }

    [VerifyTest]
    public void CollectionSyntax_SeparatesItemsWithCommas()
    {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add("one");
        collection.Add("two");

        Verify(Render(collection) == "[\"one\", \"two\"]");
    }

    [VerifyTest]
    public void CollectionSyntax_WritesNonStringItemsVerbatim()
    {
        var collection = new CollectionSyntaxDeclaration();
        collection.Add(1);
        collection.Add(2);

        Verify(Render(collection) == "[1, 2]");
    }

    [VerifyTest]
    public void CollectionSyntax_WithTheSameItems_IsEqual()
    {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        var second = new CollectionSyntaxDeclaration();
        second.Add("a");

        Verify(first.Equals(second));
    }

    [VerifyTest]
    public void CollectionSyntax_WithDifferentItems_IsNotEqual()
    {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        var second = new CollectionSyntaxDeclaration();
        second.Add("b");

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void CollectionSyntax_WithDifferentCounts_IsNotEqual()
    {
        var first = new CollectionSyntaxDeclaration();
        first.Add("a");

        Verify(!first.Equals(new CollectionSyntaxDeclaration()));
    }

    [VerifyTest]
    public void CollectionSyntax_IsNotEqualToOtherTypes()
    {
        Verify(!new CollectionSyntaxDeclaration().Equals("not a collection"));
    }

    private static AttributeModel Attribute(
        IReadOnlyList<AttributeArgumentValue>? arguments = null,
        IReadOnlyList<AttributeArgumentValue>? properties = null
    ) => new(SomeType, arguments ?? [], properties ?? [], []);

    private static string Render(IEnumerable<IOutputComponent> components)
    {
        var context = new OutputContext(
            new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global }
        );

        foreach (var component in components)
        {
            component.WriteOutput(context);
        }

        return context.Output();
    }

    private static string Render(IOutputComponent component) => Render([component]);
}

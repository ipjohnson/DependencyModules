using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// The generator's model records feed Roslyn's incremental cache, so two structurally identical
/// models built on consecutive runs must compare equal. A positional record compares
/// IReadOnlyList members by reference, which silently disables caching — these tests pin the
/// structural semantics that replaced it.
/// </summary>
public partial class ModelEqualityTests
{
    private static readonly ITypeDefinition SomeType = TypeDefinition.Get("Ns", "SomeType");
    private static readonly ITypeDefinition OtherType = TypeDefinition.Get("Ns", "OtherType");

    [VerifyTest]
    public void AttributeModel_WithSeparateButEqualLists_IsEqual()
    {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "value")]);
        var second = Attribute(arguments: [new AttributeArgumentValue("key", "value")]);

        Verify(first.Equals(second));
        Verify(first.GetHashCode() == second.GetHashCode());
    }

    [VerifyTest]
    public void AttributeModel_WithEmptyLists_IsEqual()
    {
        Verify(Attribute().Equals(Attribute()));
    }

    [VerifyTest]
    public void AttributeModel_WithDifferentArguments_IsNotEqual()
    {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "one")]);
        var second = Attribute(arguments: [new AttributeArgumentValue("key", "two")]);

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeModel_WithDifferentArgumentCounts_IsNotEqual()
    {
        var first = Attribute(arguments: [new AttributeArgumentValue("key", "one")]);
        var second = Attribute();

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeModel_WithDifferentProperties_IsNotEqual()
    {
        var first = Attribute(properties: [new AttributeArgumentValue("P", 1)]);
        var second = Attribute(properties: [new AttributeArgumentValue("P", 2)]);

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeModel_WithDifferentImplementedInterfaces_IsNotEqual()
    {
        var first = Attribute(interfaces: [SomeType]);
        var second = Attribute(interfaces: [OtherType]);

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeModel_WithDifferentType_IsNotEqual()
    {
        var first = new AttributeModel(SomeType, [], [], []);
        var second = new AttributeModel(OtherType, [], [], []);

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeArgumentValue_WithEqualArrayValues_IsEqual()
    {
        var first = new AttributeArgumentValue("names", new[] { "a", "b" });
        var second = new AttributeArgumentValue("names", new[] { "a", "b" });

        Verify(first.Equals(second));
        Verify(first.GetHashCode() == second.GetHashCode());
    }

    [VerifyTest]
    public void AttributeArgumentValue_WithDifferentArrayValues_IsNotEqual()
    {
        var first = new AttributeArgumentValue("names", new[] { "a", "b" });
        var second = new AttributeArgumentValue("names", new[] { "a", "c" });

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeArgumentValue_WithDifferentArrayLengths_IsNotEqual()
    {
        var first = new AttributeArgumentValue("names", new[] { "a" });
        var second = new AttributeArgumentValue("names", new[] { "a", "b" });

        Verify(!first.Equals(second));
    }

    [VerifyTest]
    public void AttributeArgumentValue_WithDifferentNames_IsNotEqual()
    {
        Verify(
            !new AttributeArgumentValue("one", "value").Equals(
                new AttributeArgumentValue("two", "value")
            )
        );
    }

    [VerifyTest]
    public void AttributeArgumentValue_WithNullValues_IsEqual()
    {
        Verify(
            new AttributeArgumentValue("key", null).Equals(new AttributeArgumentValue("key", null))
        );
    }

    [VerifyTest]
    public void AttributeArgumentValue_NullVersusValue_IsNotEqual()
    {
        Verify(
            !new AttributeArgumentValue("key", null).Equals(
                new AttributeArgumentValue("key", "value")
            )
        );
    }

    /// <summary>
    /// A string is IEnumerable; it must not be compared character by character against a
    /// char collection.
    /// </summary>
    [VerifyTest]
    public void AttributeArgumentValue_StringVersusCharArray_IsNotEqual()
    {
        Verify(
            !new AttributeArgumentValue("key", "ab").Equals(
                new AttributeArgumentValue("key", new[] { 'a', 'b' })
            )
        );
    }

    [VerifyTest]
    public void ParameterInfoModel_WithSeparateButEqualAttributes_IsEqual()
    {
        var first = new ParameterInfoModel("name", SomeType, null, [Attribute()]);
        var second = new ParameterInfoModel("name", SomeType, null, [Attribute()]);

        Verify(first.Equals(second));
        Verify(first.GetHashCode() == second.GetHashCode());
    }

    [VerifyTest]
    public void ParameterInfoModel_WithDifferentNames_IsNotEqual()
    {
        Verify(
            !new ParameterInfoModel("one", SomeType, null, []).Equals(
                new ParameterInfoModel("two", SomeType, null, [])
            )
        );
    }

    [VerifyTest]
    public void ParameterInfoModel_WithDifferentTypes_IsNotEqual()
    {
        Verify(
            !new ParameterInfoModel("name", SomeType, null, []).Equals(
                new ParameterInfoModel("name", OtherType, null, [])
            )
        );
    }

    [VerifyTest]
    public void ParameterInfoModel_WithDifferentDefaultValues_IsNotEqual()
    {
        Verify(
            !new ParameterInfoModel("name", SomeType, 1, []).Equals(
                new ParameterInfoModel("name", SomeType, 2, [])
            )
        );
    }

    [VerifyTest]
    public void ConstructorInfoModel_WithSeparateButEqualParameters_IsEqual()
    {
        var first = new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]);
        var second = new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]);

        Verify(first.Equals(second));
        Verify(first.GetHashCode() == second.GetHashCode());
    }

    [VerifyTest]
    public void ConstructorInfoModel_WithDifferentParameters_IsNotEqual()
    {
        Verify(
            !new ConstructorInfoModel([new ParameterInfoModel("a", SomeType, null, [])]).Equals(
                new ConstructorInfoModel([new ParameterInfoModel("b", SomeType, null, [])])
            )
        );
    }

    [VerifyTest]
    public void ServiceFactoryModel_WithSeparateButEqualParameters_IsEqual()
    {
        var first = new ServiceFactoryModel(
            SomeType,
            "Create",
            [new ParameterInfoModel("a", SomeType, null, [])]
        );
        var second = new ServiceFactoryModel(
            SomeType,
            "Create",
            [new ParameterInfoModel("a", SomeType, null, [])]
        );

        Verify(first.Equals(second));
        Verify(first.GetHashCode() == second.GetHashCode());
    }

    [VerifyTest]
    public void ServiceFactoryModel_WithDifferentMethodNames_IsNotEqual()
    {
        Verify(
            !new ServiceFactoryModel(SomeType, "Create", []).Equals(
                new ServiceFactoryModel(SomeType, "Build", [])
            )
        );
    }

    [VerifyTest]
    public void ServiceFactoryModel_WithDifferentDeclaringTypes_IsNotEqual()
    {
        Verify(
            !new ServiceFactoryModel(SomeType, "Create", []).Equals(
                new ServiceFactoryModel(OtherType, "Create", [])
            )
        );
    }

    private static AttributeModel Attribute(
        IReadOnlyList<AttributeArgumentValue>? arguments = null,
        IReadOnlyList<AttributeArgumentValue>? properties = null,
        IReadOnlyList<ITypeDefinition>? interfaces = null
    ) =>
        new(
            SomeType,
            arguments ?? new List<AttributeArgumentValue>(),
            properties ?? new List<AttributeArgumentValue>(),
            interfaces ?? new List<ITypeDefinition>()
        );
}

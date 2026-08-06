using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// TypeParameterDefinition stands in for an open generic parameter (the T in Repository&lt;T&gt;),
/// which has no namespace and must be written out by name rather than fully qualified.
/// </summary>
public class TypeParameterDefinitionTests {

    private static TypeParameterDefinition Parameter(string name = "T") =>
        new(TypeDefinitionEnum.ClassDefinition, isNullable: false, isArray: false, name);

    [Fact]
    public void ExposesTheNameItWasGiven() {
        Assert.Equal("TValue", Parameter("TValue").Name);
    }

    [Fact]
    public void HasNoNamespace() {
        Assert.Equal("", Parameter().Namespace);
    }

    [Fact]
    public void HasNoKnownNamespaces() {
        Assert.Empty(Parameter().KnownNamespaces);
    }

    [Fact]
    public void HasNoTypeArguments() {
        Assert.Empty(Parameter().TypeArguments);
    }

    [Theory]
    [InlineData(TypeOutputMode.ShortName)]
    [InlineData(TypeOutputMode.Global)]
    public void WritesItsBareNameInEveryOutputMode(TypeOutputMode mode) {
        var builder = new StringBuilder();

        Parameter("TValue").WriteTypeName(builder, mode);

        Assert.Equal("TValue", builder.ToString());
    }

    [Fact]
    public void MakeNullable_KeepsTheNameAndMarksItNullable() {
        var nullable = Parameter("TValue").MakeNullable();

        Assert.True(nullable.IsNullable);
        Assert.Equal("TValue", nullable.Name);
    }

    [Fact]
    public void MakeNullable_CanClearNullability() {
        var notNullable = Parameter().MakeNullable().MakeNullable(false);

        Assert.False(notNullable.IsNullable);
    }

    [Fact]
    public void MakeNullable_PreservesArrayness() {
        var nullableArray = Parameter().MakeArray().MakeNullable();

        Assert.True(nullableArray.IsArray);
        Assert.True(nullableArray.IsNullable);
    }

    [Fact]
    public void MakeArray_KeepsTheNameAndMarksItAnArray() {
        var array = Parameter("TValue").MakeArray();

        Assert.True(array.IsArray);
        Assert.Equal("TValue", array.Name);
    }

    [Fact]
    public void MakeArray_PreservesNullability() {
        var nullableArray = Parameter().MakeNullable().MakeArray();

        Assert.True(nullableArray.IsNullable);
        Assert.True(nullableArray.IsArray);
    }

    [Fact]
    public void PreservesTheTypeDefinitionKind() {
        var parameter = new TypeParameterDefinition(
            TypeDefinitionEnum.InterfaceDefinition, isNullable: false, isArray: false, "TValue");

        Assert.Equal(TypeDefinitionEnum.InterfaceDefinition, parameter.MakeArray().TypeDefinitionEnum);
    }

    [Fact]
    public void CompareTo_MatchesAnotherParameterWithTheSameName() {
        Assert.Equal(0, Parameter("T").CompareTo(Parameter("T")));
    }

    [Fact]
    public void CompareTo_DoesNotMatchADifferentName() {
        Assert.NotEqual(0, Parameter("T").CompareTo(Parameter("TOther")));
    }

    [Fact]
    public void CompareTo_DoesNotMatchAConcreteType() {
        Assert.NotEqual(0, Parameter("T").CompareTo(TypeDefinition.Get("Ns", "T")));
    }
}

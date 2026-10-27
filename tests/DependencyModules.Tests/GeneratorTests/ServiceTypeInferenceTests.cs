using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// ServiceTypeInference called on symbols, the way an analyzer calls it.
/// </summary>
public class ServiceTypeInferenceTests
{
    [Fact]
    public void FirstRoleInterface_IsTheServiceType()
    {
        var serviceType = Infer(
            """
            public interface IThing;

            public class Thing : System.IDisposable, IThing {
                public void Dispose() { }
            }
            """
        );

        Assert.Equal("IThing", serviceType.Name);
    }

    [Fact]
    public void InterfaceOfABaseClass_IsTheServiceType()
    {
        var serviceType = Infer(
            """
            public interface IThing;

            public abstract class ThingBase : IThing;

            public class Thing : ThingBase;
            """
        );

        Assert.Equal("IThing", serviceType.Name);
    }

    [Fact]
    public void OpenGenericInterface_HasItsTypeParametersEmptied()
    {
        var serviceType = Infer(
            """
            public interface IGeneric<T>;

            public class Thing<T> : IGeneric<T>;
            """
        );

        Assert.Equal("IGeneric", serviceType.Name);
        Assert.All(serviceType.TypeArguments, argument => Assert.Equal("", argument.Name));
    }

    /// <summary>
    /// Only the base list of the declaration that carries the attribute is read.
    /// </summary>
    [Fact]
    public void InterfaceOnAnotherPartialDeclaration_IsNotSeen()
    {
        var serviceType = Infer(
            """
            public interface IThing;

            public partial class Thing;

            public partial class Thing : IThing;
            """
        );

        Assert.Equal("Thing", serviceType.Name);
    }

    private static ITypeDefinition Infer(string source)
    {
        var tree = CSharpSyntaxTree.ParseText("namespace TestNamespace;\n" + source);
        var compilation = CSharpCompilation.Create(
            "InferenceTest",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(type => type.Identifier.Text == "Thing");

        return ServiceTypeInference.GetServiceType(
            TypeDefinition.Get("TestNamespace", "Thing"),
            ServiceTypeInference.BaseListSymbols(declaration, compilation.GetSemanticModel(tree))
        );
    }
}

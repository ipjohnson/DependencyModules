using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Puts <c>[ExcludeFromCodeCoverage]</c> on the members of the classes in a generated file.
/// </summary>
/// <remarks>
/// Never on a class. A module is a partial class, and an attribute on one part applies to the
/// whole type, so coverage tools then skip the members that the developer writes too.
/// </remarks>
internal static class GeneratedCodeCoverage
{
    private static readonly ITypeDefinition ExcludeFromCodeCoverage = TypeDefinition.Get(
        "System.Diagnostics.CodeAnalysis",
        "ExcludeFromCodeCoverage"
    );

    public static void ExcludeMembers(
        CSharpFileDefinition file,
        DependencyModuleConfigurationModel configurationModel
    )
    {
        if (!configurationModel.ExcludeGeneratedCodeFromCoverage)
        {
            return;
        }

        foreach (var classDefinition in file.GetAllNamedComponents().OfType<ClassDefinition>())
        {
            foreach (var method in classDefinition.Methods)
            {
                method.AddAttribute(ExcludeFromCodeCoverage);
            }

            foreach (var constructor in classDefinition.Constructors)
            {
                constructor.AddAttribute(ExcludeFromCodeCoverage);
            }

            foreach (var property in classDefinition.Properties)
            {
                property.AddAttribute(ExcludeFromCodeCoverage);
            }
        }
    }
}

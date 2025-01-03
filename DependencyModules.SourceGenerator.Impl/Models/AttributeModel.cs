using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record AttributeModel (
    ITypeDefinition TypeDefinition,
    string Arguments,
    string PropertyAssignment);
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record ParameterInfoModel(
    string ParameterName,
    ITypeDefinition ParameterType,
    object? DefaultValue,
    IReadOnlyList<AttributeModel> Attributes);
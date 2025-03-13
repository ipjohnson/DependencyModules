using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public interface IClassModel {
    ITypeDefinition ClassType { get; }
    
    IReadOnlyList<ParameterInfoModel> Parameters { get; }
    IReadOnlyList<PropertyInfoModel> PropertyInfoModels { get; }
    IReadOnlyList<AttributeModel> AttributeModels { get; }
}
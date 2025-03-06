namespace DependencyModules.SourceGenerator.Impl.Models;

public interface IClassModel {
    IReadOnlyList<ParameterInfoModel> Parameters { get; }
    IReadOnlyList<PropertyInfoModel> PropertyInfoModels { get; }
    IReadOnlyList<AttributeModel> AttributeModels { get; }
}
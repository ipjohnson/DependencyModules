using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public enum AccessModifier {
    PublicModifier,
    PrivateModifier,
    ProtectedModifier,
    InternalModifier,
}

public record MethodInfoModel(
    AccessModifier AccessModifier,
    string MethodName,
    ITypeDefinition ReturnType,
    IReadOnlyList<ParameterInfoModel> Parameters,
    IReadOnlyList<ITypeDefinition> GenericArguments);
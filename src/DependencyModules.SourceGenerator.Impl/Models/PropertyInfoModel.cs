using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record PropertyInfoModel(ITypeDefinition PropertyType, string PropertyName, bool IsReadOnly, bool IsStatic);
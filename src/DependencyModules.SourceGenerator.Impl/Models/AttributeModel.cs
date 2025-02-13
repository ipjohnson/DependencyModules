using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record AttributeArgumentValue(string Name, object? Value);

public record AttributeModel(
    ITypeDefinition TypeDefinition,
    IReadOnlyList<AttributeArgumentValue> Arguments,
    IReadOnlyList<AttributeArgumentValue> Properties) {
    
    public string ArgumentString => string.Join(", ", Arguments.Select(SelectArgument));
    
    public string PropertyString => string.Join(", ", Properties.Select(SelectProperty));

    private string SelectProperty(AttributeArgumentValue property) {
        var value = property.Value;

        if (value is string stringValue) {
            return $" {property.Name} = \"{stringValue}\"";
        }
        
        return $" {property.Name} = {property.Value}";
    }

    private string SelectArgument(AttributeArgumentValue argument) {
        var value = argument.Value;

        if (value is string stringValue) {
            value = $"\"{stringValue}\"";
        }
        
        if (string.IsNullOrEmpty(argument.Name)) {
            return " " +value;
        }
        
        return $" {argument.Name}: {value}";
    }
}

public record AttributeClassInfo(
    IReadOnlyList<ParameterInfoModel> ConstructorParameters, IReadOnlyList<PropertyInfoModel> Properties);
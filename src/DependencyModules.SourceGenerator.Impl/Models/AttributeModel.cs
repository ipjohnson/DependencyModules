using System.Text;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record AttributeArgumentValue(string Name, object? Value);

public record AttributeModel(
    ITypeDefinition TypeDefinition,
    IReadOnlyList<AttributeArgumentValue> Arguments,
    IReadOnlyList<AttributeArgumentValue> Properties,
    IReadOnlyList<ITypeDefinition> ImplementedInterfaces) {

    public object[] ArgumentString() {
        var list = new List<object>();
        foreach (var argument in Arguments) {
            IOutputComponent? outputComponent = null;

            if (argument.Value is IOutputComponent component) {
                outputComponent = component;
            }
            else if (argument.Value is string stringValue) {
                outputComponent = CodeOutputComponent.Get(
                    SyntaxHelpers.QuoteString(stringValue)
                );
            }
            else if (argument.Value is not null) {
                outputComponent = CodeOutputComponent.Get(argument.Value);
            }

            if (outputComponent != null) {
                list.Add(outputComponent);
            }
        }

        return list.ToArray();
    }

    public IOutputComponent PropertyString() {
        var list = new List<IOutputComponent>();
        foreach (var argument in Properties) {

            IOutputComponent? outputComponent = null;

            if (argument.Value is IOutputComponent component) {
                outputComponent = component;
            }
            else if (argument.Value is string stringValue) {
                outputComponent = CodeOutputComponent.Get(
                    SyntaxHelpers.QuoteString(stringValue)
                );
            }
            else if (argument.Value is not null) {
                outputComponent = CodeOutputComponent.Get(argument.Value);
            }

            if (outputComponent != null) {
                list.Add(
                    new WrapStatement(
                        CodeOutputComponent.Get(" = "),
                        CodeOutputComponent.Get(argument.Name),
                        outputComponent));
            }
        }

        return new ListOutputComponent(list);
    }
}

public record AttributeClassInfo(
    IReadOnlyList<ParameterInfoModel> ConstructorParameters,
    IReadOnlyList<PropertyInfoModel> Properties);
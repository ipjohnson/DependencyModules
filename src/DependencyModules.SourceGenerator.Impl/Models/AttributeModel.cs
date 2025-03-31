using System.Reflection;
using System.Text;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record AttributeArgumentValue(string Name, object? Value);

public record AttributeModel(
    ITypeDefinition TypeDefinition,
    IReadOnlyList<AttributeArgumentValue> Arguments,
    IReadOnlyList<AttributeArgumentValue> Properties,
    IReadOnlyList<ITypeDefinition> ImplementedInterfaces) {

    public IList<IOutputComponent> GetArguments() {
        var list = new List<IOutputComponent>();
        foreach (var argument in Arguments) {
            IOutputComponent? outputComponent = null;

            if (argument.Value is IOutputComponent component) {
                outputComponent = component;
            }
            else if (argument.Value is Array arrayValue) {
                var collectionSyntax = new CollectionSyntaxDeclaration();

                foreach (var objectValue in arrayValue) {
                    if (objectValue is string stringValue) {
                        collectionSyntax.Add(SyntaxHelpers.QuoteString(stringValue));
                    }
                    else if (argument.Value is ITypeDefinition typeDefinition) {
                        outputComponent = SyntaxHelpers.TypeOf(typeDefinition);
                        collectionSyntax.Add(outputComponent);
                    }
                    else if (objectValue is not null) {
                        collectionSyntax.Add(CodeOutputComponent.Get(objectValue));
                    }
                }
                
                outputComponent = collectionSyntax;
            }
            else if (argument.Value is string stringValue) {
                outputComponent = CodeOutputComponent.Get(
                    SyntaxHelpers.QuoteString(stringValue)
                );
            } 
            else if (argument.Value is ITypeDefinition typeDefinition) {
                outputComponent = SyntaxHelpers.TypeOf(typeDefinition);
            }
            else if (argument.Value is not null) {
                outputComponent = CodeOutputComponent.Get(argument.Value);
            }

            if (outputComponent != null) {
                list.Add(outputComponent);
            }
        }

        return list;
    }
    

    public IList<IOutputComponent> PropertyValues() {
        var list = new List<IOutputComponent>();
        foreach (var argument in Properties) {

            IOutputComponent? outputComponent = null;

            if (argument.Value is IOutputComponent component) {
                outputComponent = component;
            }
            else if (argument.Value is Array arrayValue) {
                var collectionSyntax = new CollectionSyntaxDeclaration();

                foreach (var objectValue in arrayValue) {
                    if (objectValue is string stringValue) {
                        collectionSyntax.Add(stringValue);
                    }
                    else if (argument.Value is ITypeDefinition typeDefinition) {
                        outputComponent = SyntaxHelpers.TypeOf(typeDefinition);
                        collectionSyntax.Add(outputComponent);
                    }
                    else if (objectValue is not null) {
                        collectionSyntax.Add(CodeOutputComponent.Get(objectValue));
                    }
                }
                
                outputComponent = collectionSyntax;         
            }
            else if (argument.Value is string stringValue) {
                outputComponent = CodeOutputComponent.Get(
                    SyntaxHelpers.QuoteString(stringValue.Trim('"'))
                );
            }
            else if (argument.Value is ITypeDefinition typeDefinition) {
                outputComponent = SyntaxHelpers.TypeOf(typeDefinition);
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

        return list;
    }
}

public record AttributeClassInfo(
    ConstructorInfoModel ConstructorInfo,
    IReadOnlyList<PropertyInfoModel> Properties);
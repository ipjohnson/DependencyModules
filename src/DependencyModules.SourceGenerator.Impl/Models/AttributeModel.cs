using System.Reflection;
using System.Text;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record AttributeArgumentValue(string Name, object? Value) {
    // Value arrives as object and may be an array, which the compiler-generated record equality
    // would compare by reference. See ModelEquality for why that breaks incremental caching.
    public virtual bool Equals(AttributeArgumentValue? other) =>
        other is not null &&
        Name == other.Name &&
        ModelEquality.ValueEquals(Value, other.Value);

    public override int GetHashCode() {
        unchecked {
            return Name.GetHashCode() * 31 + ModelEquality.ValueHashCode(Value);
        }
    }
}

public record AttributeModel(
    ITypeDefinition TypeDefinition,
    IReadOnlyList<AttributeArgumentValue> Arguments,
    IReadOnlyList<AttributeArgumentValue> Properties,
    IReadOnlyList<ITypeDefinition> ImplementedInterfaces) {

    // Structural equality over the list members; see ModelEquality.
    public virtual bool Equals(AttributeModel? other) =>
        other is not null &&
        TypeDefinition.Equals(other.TypeDefinition) &&
        ModelEquality.ListEquals(Arguments, other.Arguments) &&
        ModelEquality.ListEquals(Properties, other.Properties) &&
        ModelEquality.ListEquals(ImplementedInterfaces, other.ImplementedInterfaces);

    public override int GetHashCode() {
        unchecked {
            var hash = TypeDefinition.GetHashCode();
            hash = hash * 31 + ModelEquality.ListHashCode(Arguments);
            hash = hash * 31 + ModelEquality.ListHashCode(Properties);
            hash = hash * 31 + ModelEquality.ListHashCode(ImplementedInterfaces);
            return hash;
        }
    }


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
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class AttributeModelHelper {
    public static IEnumerable<AttributeModel> GetAttributes(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeListSyntax,
        CancellationToken cancellationToken,
        Func<AttributeSyntax, bool>? filter = null) {
        foreach (var attributeList in attributeListSyntax) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = context.SemanticModel.GetTypeInfo(attribute);

                if (filter?.Invoke(attribute) ?? true) {
                    if (operation.Type != null) {
                        yield return InternalAttributeModel(attribute, operation);
                    }
                }
            }
        }
    }

    public static AttributeModel? GetAttribute(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var operation = context.SemanticModel.GetTypeInfo(attribute);

        return operation.Type != null ? InternalAttributeModel(attribute, operation) : null;
    }

    private static AttributeModel InternalAttributeModel(AttributeSyntax attribute, TypeInfo operation) {
        var arguments = "";
        var propertyAssignment = "";

        if (attribute.ArgumentList != null) {
            foreach (var attributeArgumentSyntax in
                     attribute.ArgumentList.Arguments) {
                if (attributeArgumentSyntax.ToString().Contains("=")) {
                    if (propertyAssignment.Length > 0) {
                        propertyAssignment += ", ";
                    }

                    propertyAssignment += attributeArgumentSyntax.ToString();
                }
                else {
                    if (arguments.Length > 0) {
                        arguments += ", ";
                    }

                    arguments += attributeArgumentSyntax.ToString();
                }
            }
        }

        if (operation.Type == null) {
            throw new ArgumentNullException("operation.Type", "The type argument cannot be null.");
        }

        var type = operation.Type.GetTypeDefinition();

        if (!type.Name.EndsWith("Attribute")) {
            type = TypeDefinition.Get(type.Namespace, type.Name + "Attribute");
        }

        return new AttributeModel(type,
            arguments,
            propertyAssignment);
    }
}
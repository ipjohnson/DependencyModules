using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class AttributeModelHelper {

    public static AttributeClassInfo GetAttributeClassInfo(GeneratorSyntaxContext context) {
        var propertyList = new List<PropertyInfoModel>();
        var constructors = new List<ConstructorDeclarationSyntax>();

        foreach (var syntax in
                 context.Node.DescendantNodes()) {
            if (syntax is PropertyDeclarationSyntax propertyDeclarationSyntax) {
                var setter =
                    propertyDeclarationSyntax.AccessorList?.Accessors.FirstOrDefault(
                        x => x.IsKind(SyntaxKind.SetAccessorDeclaration));

                var propertyType =
                    propertyDeclarationSyntax.Type.GetTypeDefinition(context);

                if (propertyType != null) {
                    propertyList.Add(new PropertyInfoModel(propertyType,
                        propertyDeclarationSyntax.Identifier.ToString(),
                        setter == null
                    ));
                }
            }
            else if (syntax is ConstructorDeclarationSyntax constructorDeclarationSyntax) {
                constructors.Add(constructorDeclarationSyntax);
            }
        }

        return new AttributeClassInfo(
            GetConstructorParameterList(context, constructors),
            propertyList);
    }

    private static IReadOnlyList<ParameterInfoModel> GetConstructorParameterList(
        GeneratorSyntaxContext context, List<ConstructorDeclarationSyntax> constructors) {
        constructors.Sort(
            (a, b) =>
                a.ParameterList.Parameters.Count.CompareTo(b.ParameterList.Parameters.Count));

        if (constructors.Count == 0) {
            return Array.Empty<ParameterInfoModel>();
        }

        var list = new List<ParameterInfoModel>();

        foreach (var parameterSyntax in constructors[0].ParameterList.Parameters) {
            list.Add(new ParameterInfoModel(
                parameterSyntax.Identifier.ToString(),
                parameterSyntax.Type?.GetTypeDefinition(context) ?? TypeDefinition.Get(typeof(object))
            ));
        }

        return list;
    }

    public static IEnumerable<AttributeModel> GetAttributes(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeListSyntax,
        CancellationToken cancellationToken,
        Func<AttributeSyntax, bool>? filter = null) {
        foreach (var attributeList in attributeListSyntax) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = ModelExtensions.GetTypeInfo(context.SemanticModel, attribute);

                if (filter?.Invoke(attribute) ?? true) {
                    if (operation.Type != null) {
                        yield return InternalAttributeModel(context, attribute, operation);
                    }
                }
            }
        }
    }

    public static AttributeModel? GetAttribute(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var operation = ModelExtensions.GetTypeInfo(context.SemanticModel, attribute);

        return operation.Type != null ? InternalAttributeModel(context, attribute, operation) : null;
    }

    private static AttributeModel InternalAttributeModel(
        GeneratorSyntaxContext context, AttributeSyntax attribute, TypeInfo operation) {
        var arguments = new List<AttributeArgumentValue>();
        var properties = new List<AttributeArgumentValue>();


        if (attribute.ArgumentList != null) {
            foreach (var attributeArgumentSyntax in
                     attribute.ArgumentList.Arguments) {
                if (attributeArgumentSyntax.NameColon != null) {
                    var constantValue =
                        context.SemanticModel.GetOperation(attributeArgumentSyntax.Expression)?.ConstantValue.Value;

                    arguments.Add(
                        new AttributeArgumentValue(
                            attributeArgumentSyntax.NameColon.ToString(),
                            constantValue
                        ));

                }
                else if (attributeArgumentSyntax.NameEquals != null) {
                    var constantValue =
                        context.SemanticModel.GetOperation(attributeArgumentSyntax.Expression)?.ConstantValue.Value;
                    var name =
                        attributeArgumentSyntax.NameEquals.Name.ToString().Replace("=", "").Trim();

                    properties.Add(
                        new AttributeArgumentValue(
                            name,
                            constantValue
                        ));
                }
                else {
                    var constantValue =
                        context.SemanticModel.GetOperation(attributeArgumentSyntax.Expression)?.ConstantValue.Value;

                    arguments.Add(
                        new AttributeArgumentValue(
                            "",
                            constantValue
                        ));
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
            properties);
    }


}
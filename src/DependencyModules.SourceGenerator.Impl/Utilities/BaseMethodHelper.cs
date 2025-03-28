using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class BaseMethodHelper {
    public static IReadOnlyList<ParameterInfoModel> GetMethodParameters(
        this BaseMethodDeclarationSyntax methodDeclarationSyntax,
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
        ) {
        var parameterList = methodDeclarationSyntax.ParameterList;

        return GetParameters(parameterList, context, cancellationToken);
    }

    public static IReadOnlyList<ParameterInfoModel> GetParameters(this ParameterListSyntax? parameterList, GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        if (parameterList == null) {
            return Array.Empty<ParameterInfoModel>();
        }
   
        var list = new List<ParameterInfoModel>();

        foreach (var parameterSyntax in parameterList.Parameters) {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(new ParameterInfoModel(
                parameterSyntax.Identifier.ToString(),
                parameterSyntax.Type?.GetTypeDefinition(context) ?? TypeDefinition.Get(typeof(object)),
                null,
                AttributeModelHelper.GetAttributeModels(context, parameterSyntax, cancellationToken)
            ));
        }

        return list;
    }
}
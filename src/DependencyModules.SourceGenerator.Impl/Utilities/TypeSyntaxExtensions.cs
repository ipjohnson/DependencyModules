using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class TypeSyntaxExtensions {
    public static ITypeDefinition? GetTypeDefinition(this SyntaxNode typeSyntax,
        GeneratorSyntaxContext generatorSyntaxContext) {
        var symbolInfo = generatorSyntaxContext.SemanticModel.GetSymbolInfo(typeSyntax);

        var type = GetTypeDefinitionFromSymbolInfo(symbolInfo);

        if (typeSyntax.ToString().EndsWith("?")) {
            return type?.MakeNullable();
        }

        return type;
    }

    public static ITypeDefinition? GetTypeDefinition(this MemberAccessExpressionSyntax syntax, GeneratorSyntaxContext generatorSyntaxContext) {
        var typeInfo = generatorSyntaxContext.SemanticModel.GetSymbolInfo(syntax.Expression);
        
        if (typeInfo.Symbol is INamedTypeSymbol namedTypeSymbol) {
            return GetTypeDefinition(namedTypeSymbol);
        }
        
        return null;
    }
    
    public static string GetFullName(this INamespaceSymbol? namespaceSymbol) {
        if (namespaceSymbol == null) {
            return "";
        }

        var baseString = namespaceSymbol.ContainingNamespace?.GetFullName();

        if (string.IsNullOrEmpty(baseString)) {
            return namespaceSymbol.Name;
        }

        return baseString + "." + namespaceSymbol.Name;
    }

    public static ITypeDefinition GetTypeDefinition(this ITypeSymbol typeSymbol) {
        if (typeSymbol is INamedTypeSymbol namedTypeSymbol) {
            return InternalGetTypeDefinitionFromNamedSymbol(namedTypeSymbol);
        }
        
        var typeEnum = GetTypeSymbolKind(typeSymbol);

        return TypeDefinition.Get(typeEnum, typeSymbol.ContainingNamespace.GetFullName(), GetTypeName(typeSymbol));
    }

    private static TypeDefinitionEnum GetTypeSymbolKind(ITypeSymbol typeSymbol) {
        var typeEnum = TypeDefinitionEnum.ClassDefinition;

        if (typeSymbol.TypeKind == TypeKind.Enum) {
            typeEnum = TypeDefinitionEnum.EnumDefinition;
        }
        else if (typeSymbol.TypeKind == TypeKind.Interface) {
            typeEnum = TypeDefinitionEnum.InterfaceDefinition;
        }

        return typeEnum;
    }

    private static string GetTypeName(ITypeSymbol typeSymbol) {
        if (typeSymbol.ContainingType != null) {
            return GetTypeName(typeSymbol.ContainingType) + "." + typeSymbol.Name;
        }

        return typeSymbol.Name;
    }

    public static ITypeDefinition? GetTypeDefinitionFromSymbolInfo(SymbolInfo symbolInfo) {
        if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol) {
            return GetTypeDefinitionFromNamedSymbol(namedTypeSymbol);
        }

        if (symbolInfo.Symbol is IArrayTypeSymbol arrayTypeSymbol) {
            return GetTypeDefinitionFromType(arrayTypeSymbol.ElementType).MakeArray();
        }
        
        return null;
    }
    
    public static ITypeDefinition? GetTypeDefinitionFromNamedSymbol(this INamedTypeSymbol? namedTypeSymbol) {
        if (namedTypeSymbol == null) {
            return null;
        }
        
        return InternalGetTypeDefinitionFromNamedSymbol(namedTypeSymbol);
    }
    
    private static ITypeDefinition InternalGetTypeDefinitionFromNamedSymbol(INamedTypeSymbol namedTypeSymbol) {

        if (namedTypeSymbol.IsGenericType) {
            if (namedTypeSymbol.Name == "Nullable") {
                var baseType = namedTypeSymbol.TypeArguments.First();
                return GetTypeDefinitionFromType(baseType).MakeNullable();
            }

            var closingTypeSymbols = namedTypeSymbol.TypeArguments;

            var closingTypes = new List<ITypeDefinition>();

            foreach (var typeSymbol in closingTypeSymbols) {
                var finalType = GetTypeDefinitionFromType(typeSymbol);

                closingTypes.Add(finalType);
            }

            var genericType = new GenericTypeDefinition(
                GetTypeSymbolKind(namedTypeSymbol),
                namedTypeSymbol.ContainingNamespace.GetFullName(),
                GetTypeName(namedTypeSymbol),
                closingTypes
            );

            if (namedTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated) {
                return genericType.MakeNullable();
            }

            return genericType;
        }
        if (IsKnownType(namedTypeSymbol.Name)) { }

        var ns = namedTypeSymbol.ContainingNamespace.GetFullName();
        var getName = GetTypeName(namedTypeSymbol);

        var typeDef = TypeDefinition.Get(
            GetTypeSymbolKind(namedTypeSymbol),
            namedTypeSymbol.ContainingNamespace.GetFullName(), GetTypeName(namedTypeSymbol));

        if (namedTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated) {
            return typeDef.MakeNullable();
        }

        return typeDef;
    }

    private static ITypeDefinition GetTypeDefinitionFromType(ITypeSymbol typeSymbol) {
        switch (typeSymbol.SpecialType) {
            case SpecialType.System_Int16:
                return TypeDefinition.Get(typeof(short));

            case SpecialType.System_Int32:
                return TypeDefinition.Get(typeof(int));

            case SpecialType.System_Int64:
                return TypeDefinition.Get(typeof(long));

            case SpecialType.System_UInt16:
                return TypeDefinition.Get(typeof(ushort));

            case SpecialType.System_UInt32:
                return TypeDefinition.Get(typeof(uint));

            case SpecialType.System_UInt64:
                return TypeDefinition.Get(typeof(ulong));

            case SpecialType.System_String:
                return TypeDefinition.Get(typeof(string));
        }
        
        if (typeSymbol is ITypeParameterSymbol || typeSymbol is IErrorTypeSymbol) {
            return new TypeParameterDefinition(
                TypeDefinitionEnum.ClassDefinition, 
                false, 
                false, 
                typeSymbol.Name);
        }

        if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol) {
            return GetTypeDefinitionFromType(arrayTypeSymbol.ElementType).MakeArray();
        }

        if (typeSymbol is INamedTypeSymbol namedTypeSymbol) {
            return GetTypeDefinitionFromNamedSymbol(namedTypeSymbol)!;
        }

        return TypeDefinition.Get(typeSymbol.ContainingNamespace.GetFullName(), typeSymbol.Name);
    }

    private static bool IsKnownType(string name) {
        return false;
    }
}
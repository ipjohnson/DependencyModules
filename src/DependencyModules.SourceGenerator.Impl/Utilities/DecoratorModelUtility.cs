using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Builds <see cref="DecoratorModel"/> instances from the two declaration surfaces:
/// <c>[Decorator]</c> on the decorator class, and <c>[Decorate]</c> on a module.
/// </summary>
public static class DecoratorModelUtility
{
    /// <summary>
    /// Reads a <c>[Decorator]</c> class declaration.
    /// </summary>
    public static DecoratorModel? GetDecoratorModel(
        SyntaxTransformContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax)
        {
            return null;
        }

        var attribute = FindAttribute(typeDeclarationSyntax, "Decorator");

        if (attribute == null)
        {
            return null;
        }

        var decoratorType = GetDeclaredType(typeDeclarationSyntax, context);
        var implemented = GetImplementedTypes(typeDeclarationSyntax, context, cancellationToken);

        var order = 0;
        ITypeDefinition? realm = null;
        ITypeDefinition? explicitService = null;
        ITypeDefinition? implementation = null;

        if (attribute.ArgumentList != null)
        {
            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                switch (argument.NameEquals?.Name.ToString())
                {
                    case "Order":
                        if (int.TryParse(argument.Expression.ToString(), out var parsed))
                        {
                            order = parsed;
                        }
                        break;
                    case "Service":
                        explicitService = GetTypeOfArgument(argument, context);
                        break;
                    case "Realm":
                        realm = GetTypeOfArgument(argument, context);
                        break;
                    case "Implementation":
                        implementation = GetTypeOfArgument(argument, context);
                        break;
                }
            }
        }

        var written =
            explicitService ?? InferDecoratedService(typeDeclarationSyntax, context, implemented);

        if (written == null)
        {
            return Ignored(
                decoratorType,
                typeDeclarationSyntax,
                "It implements no type that one of its constructor parameters takes, so it has no "
                    + "service to decorate. Set Service on the attribute to name one"
            );
        }

        var serviceType = written;

        // A generic decorator decorates the open service. Its base list names the service closed over
        // its own type parameters, as IHandler<T>; the unbound IHandler<> is the form the model
        // carries, and the closed constructions to emit against are worked out from the registrations.
        if (decoratorType is GenericTypeDefinition { TypeArguments.Count: > 0 })
        {
            serviceType = ToUnboundGeneric(written);
        }

        // Read from the decorator class, exactly as they are for a service. A decorator is a
        // registration like any other, and one gated on Development has no other way to say so.
        var conditions = EnvironmentConditionUtility.GetConditions(
            context,
            typeDeclarationSyntax,
            cancellationToken
        );

        var constructor = ServiceModelUtility.GetConstructorInfo(
            context,
            typeDeclarationSyntax,
            cancellationToken,
            callableOnly: true
        );

        if (constructor == null)
        {
            return Ignored(
                decoratorType,
                typeDeclarationSyntax,
                "It has no constructor that generated code can call. Give it a public or internal "
                    + "constructor"
            );
        }

        var innerParameterIndex = IndexOfInnerParameter(constructor, written);

        if (innerParameterIndex < 0)
        {
            return Ignored(
                decoratorType,
                typeDeclarationSyntax,
                $"No parameter of its constructor takes '{written.Name}', so there is nowhere to "
                    + "pass the instance it wraps"
            );
        }

        if (!TypeParametersMatchService(typeDeclarationSyntax, written))
        {
            return Ignored(
                decoratorType,
                typeDeclarationSyntax,
                $"Its type parameters are not the type arguments of '{written.Name}' in the same "
                    + "order, so it cannot be closed over a registration"
            );
        }

        return new DecoratorModel(
            serviceType,
            decoratorType,
            order,
            realm,
            conditions,
            constructor,
            innerParameterIndex,
            true,
            implementation,
            LocationModel.From(typeDeclarationSyntax)
        );
    }

    /// <summary>
    /// A <c>[Decorator]</c> class that the generator does not apply, carrying the reason for DM0025.
    /// </summary>
    private static DecoratorModel Ignored(
        ITypeDefinition decoratorType,
        TypeDeclarationSyntax typeDeclarationSyntax,
        string reason
    ) =>
        DecoratorModel.Ignore with
        {
            DecoratorType = decoratorType,
            Location = LocationModel.From(typeDeclarationSyntax),
            IgnoredReason = reason,
        };

    /// <summary>
    /// Which constructor parameter takes the service being wrapped.
    /// </summary>
    /// <remarks>
    /// Matched on the service as written on the class rather than on the unbound form, because that
    /// is what the parameter is declared as — <c>IHandler&lt;TReq, TRes&gt;</c>, not
    /// <c>IHandler&lt;,&gt;</c>. Nullability is normalised away: <c>IGreeter? inner</c> is legal and
    /// carries an annotation the service type does not, and comparing them as written finds no
    /// parameter at all — which drops the decoration with nothing said.
    /// </remarks>
    private static int IndexOfInnerParameter(
        ConstructorInfoModel? constructor,
        ITypeDefinition serviceType
    )
    {
        if (constructor == null)
        {
            return -1;
        }

        var wanted = serviceType.MakeNullable(false);

        for (var i = 0; i < constructor.Parameters.Count; i++)
        {
            if (constructor.Parameters[i].ParameterType.MakeNullable(false).Equals(wanted))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether closing the service over a set of types means closing the decorator over the same set.
    /// </summary>
    /// <remarks>
    /// True for <c>Logging&lt;TReq, TRes&gt; : IHandler&lt;TReq, TRes&gt;</c> and false for anything
    /// that reorders, drops or reuses a parameter. The false cases are legal C# but cannot be
    /// monomorphised by position, and guessing at them would emit a <c>new</c> with the arguments
    /// the wrong way round — which compiles when the two types happen to be compatible. Nothing is
    /// emitted for them instead.
    /// </remarks>
    private static bool TypeParametersMatchService(
        TypeDeclarationSyntax typeDeclarationSyntax,
        ITypeDefinition serviceType
    )
    {
        var declared = typeDeclarationSyntax.TypeParameterList?.Parameters;

        if (declared is not { Count: > 0 })
        {
            return true;
        }

        if (
            serviceType is not GenericTypeDefinition generic
            || generic.TypeArguments.Count != declared.Value.Count
        )
        {
            return false;
        }

        for (var i = 0; i < declared.Value.Count; i++)
        {
            if (generic.TypeArguments[i].Name != declared.Value[i].Identifier.Text)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the <c>[Decorate(service, decorator)]</c> attributes declared on a module.
    /// </summary>
    public static IEnumerable<DecoratorModel> GetModuleDeclaredDecorators(
        ModuleEntryPointModel entryPointModel
    )
    {
        foreach (var attribute in entryPointModel.AttributeModels)
        {
            if (attribute.TypeDefinition.Name is not ("DecorateAttribute" or "Decorate"))
            {
                continue;
            }

            if (
                attribute.Arguments.Count < 2
                || attribute.Arguments[0].Value is not ITypeDefinition service
                || attribute.Arguments[1].Value is not ITypeDefinition decorator
            )
            {
                continue;
            }

            var order = 0;

            foreach (var property in attribute.Properties)
            {
                if (
                    property.Name == "Order"
                    && property.Value != null
                    && int.TryParse(property.Value.ToString(), out var parsed)
                )
                {
                    order = parsed;
                }
            }

            yield return new DecoratorModel(
                service,
                decorator,
                order,
                entryPointModel.EntryPointType
            );
        }
    }

    /// <summary>
    /// The decorated service is the interface the decorator both implements and accepts as a
    /// constructor parameter. That pairing is what makes something a decorator rather than merely a
    /// class with a dependency.
    /// </summary>
    private static ITypeDefinition? InferDecoratedService(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SyntaxTransformContext context,
        IReadOnlyList<ITypeDefinition> implemented
    )
    {
        if (implemented.Count == 0)
        {
            return null;
        }

        foreach (var parameterType in GetConstructorParameterTypes(typeDeclarationSyntax, context))
        {
            // Normalised, because `IGreeter? inner` is legal and its parameter type carries an
            // annotation the implemented interface does not. Compared as written, no parameter looks
            // like the service and the class stops being a decorator at all — silently.
            var declared = parameterType.MakeNullable(false);

            foreach (var candidate in implemented)
            {
                if (candidate.MakeNullable(false).Equals(declared))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<ITypeDefinition> GetConstructorParameterTypes(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SyntaxTransformContext context
    )
    {
        if (typeDeclarationSyntax.ParameterList != null)
        {
            foreach (var parameter in typeDeclarationSyntax.ParameterList.Parameters)
            {
                var type = parameter.Type?.GetTypeDefinition(context);

                if (type != null)
                {
                    yield return type;
                }
            }
        }

        foreach (
            var constructor in typeDeclarationSyntax.Members.OfType<ConstructorDeclarationSyntax>()
        )
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                var type = parameter.Type?.GetTypeDefinition(context);

                if (type != null)
                {
                    yield return type;
                }
            }
        }
    }

    /// <summary>
    /// The types a decorator can decorate: its base classes and every interface it implements.
    /// </summary>
    /// <remarks>
    /// From the symbol, not the base list as written. A decorator can get its service through a base
    /// class, or through an interface that extends it, and the base list names neither.
    /// </remarks>
    private static IReadOnlyList<ITypeDefinition> GetImplementedTypes(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SyntaxTransformContext context,
        CancellationToken cancellationToken
    )
    {
        var types = new List<ITypeDefinition>();

        if (
            context.SemanticModel.GetDeclaredSymbol(typeDeclarationSyntax, cancellationToken)
            is not INamedTypeSymbol symbol
        )
        {
            return types;
        }

        for (
            var baseType = symbol.BaseType;
            baseType is { SpecialType: not SpecialType.System_Object };
            baseType = baseType.BaseType
        )
        {
            types.Add(baseType.GetTypeDefinition());
        }

        foreach (var interfaceSymbol in symbol.AllInterfaces)
        {
            types.Add(interfaceSymbol.GetTypeDefinition());
        }

        return types;
    }

    /// <summary>
    /// Rewrites a generic type so its arguments render as the unbound <c>&lt;&gt;</c> form.
    /// </summary>
    private static ITypeDefinition ToUnboundGeneric(ITypeDefinition type)
    {
        if (type is not GenericTypeDefinition { TypeArguments.Count: > 0 } generic)
        {
            return type;
        }

        return new GenericTypeDefinition(
            generic.TypeDefinitionEnum,
            generic.Namespace,
            generic.Name,
            generic.TypeArguments.Select(_ => (ITypeDefinition)TypeDefinition.Get("", "")).ToArray()
        );
    }

    private static ITypeDefinition GetDeclaredType(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SyntaxTransformContext context
    )
    {
        var name = typeDeclarationSyntax.Identifier.ToString();

        foreach (
            var containing in typeDeclarationSyntax.Ancestors().OfType<TypeDeclarationSyntax>()
        )
        {
            name = containing.Identifier + "." + name;
        }

        var namespaceName = typeDeclarationSyntax.GetNamespace();

        if (typeDeclarationSyntax.TypeParameterList is { Parameters.Count: > 0 } parameters)
        {
            return new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                namespaceName,
                name,
                parameters.Parameters.Select(_ => TypeDefinition.Get("", "")).ToArray()
            );
        }

        return TypeDefinition.Get(namespaceName, name);
    }

    private static ITypeDefinition? GetTypeOfArgument(
        AttributeArgumentSyntax argument,
        SyntaxTransformContext context
    )
    {
        return argument.Expression is TypeOfExpressionSyntax typeOf
            ? typeOf.Type.GetTypeDefinition(context)
            : null;
    }

    private static AttributeSyntax? FindAttribute(
        TypeDeclarationSyntax typeDeclarationSyntax,
        string name
    )
    {
        foreach (var attributeList in typeDeclarationSyntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();

                if (attributeName == name || attributeName == name + "Attribute")
                {
                    return attribute;
                }
            }
        }

        return null;
    }
}

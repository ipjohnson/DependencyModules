using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Closes a generic decorator over the type arguments one registration used.
/// </summary>
/// <remarks>
/// This is the whole of monomorphisation. <c>Logging&lt;TReq, TRes&gt;</c> decorating
/// <c>IHandler&lt;TReq, TRes&gt;</c>, against a registration of
/// <c>IHandler&lt;CreateOrder, OrderId&gt;</c>, becomes a decoration of that closed service by
/// <c>Logging&lt;CreateOrder, OrderId&gt;</c> — with every constructor parameter substituted too, so
/// a decorator taking <c>IValidator&lt;TReq&gt;</c> resolves <c>IValidator&lt;CreateOrder&gt;</c>
/// rather than something the compiler would reject.
/// </remarks>
public static class DecoratorTypeUtility {

    /// <summary>
    /// The decoration to emit for one closed registration, or null when the decorator cannot be
    /// closed over it.
    /// </summary>
    public static DecoratorModel? Close(DecoratorModel decorator, GenericTypeDefinition closedService) {
        if (!decorator.CanMonomorphise) {
            return null;
        }

        var parameterNames = TypeParameterNames(decorator);

        if (parameterNames == null || parameterNames.Count != closedService.TypeArguments.Count) {
            return null;
        }

        var substitutions = new Dictionary<string, ITypeDefinition>(parameterNames.Count);

        for (var i = 0; i < parameterNames.Count; i++) {
            substitutions[parameterNames[i]] = closedService.TypeArguments[i];
        }

        var parameters = new List<ParameterInfoModel>(decorator.Constructor!.Parameters.Count);

        foreach (var parameter in decorator.Constructor.Parameters) {
            parameters.Add(parameter with {
                ParameterType = Substitute(parameter.ParameterType, substitutions)
            });
        }

        return decorator with {
            ServiceType = closedService,
            DecoratorType = CloseDecorator(decorator.DecoratorType, closedService.TypeArguments),
            Constructor = new ConstructorInfoModel(parameters)
        };
    }

    /// <summary>
    /// The decorator's type parameter names, in order.
    /// </summary>
    /// <remarks>
    /// Read off the constructor parameter that takes the service being wrapped, because that is the
    /// one place the service still appears as written — <c>IHandler&lt;TReq, TRes&gt;</c>. The model's
    /// own service and decorator types have had their arguments blanked, since neither an unbound
    /// generic nor a type parameter is a legal <c>typeof</c> argument.
    ///
    /// Safe only because <see cref="DecoratorModel.TypeParametersMatchService"/> is true, which is
    /// what guarantees these names are also the decorator's own parameters, in the same order.
    /// </remarks>
    private static IReadOnlyList<string>? TypeParameterNames(DecoratorModel decorator) {
        var inner = decorator.Constructor!.Parameters[decorator.InnerParameterIndex].ParameterType;

        if (inner is not GenericTypeDefinition generic || generic.TypeArguments.Count == 0) {
            return null;
        }

        var names = new List<string>(generic.TypeArguments.Count);

        foreach (var argument in generic.TypeArguments) {
            if (string.IsNullOrEmpty(argument.Name)) {
                return null;
            }

            names.Add(argument.Name);
        }

        return names;
    }

    private static ITypeDefinition CloseDecorator(
        ITypeDefinition decoratorType, IReadOnlyList<ITypeDefinition> typeArguments) =>
        decoratorType is GenericTypeDefinition generic
            ? new GenericTypeDefinition(
                generic.TypeDefinitionEnum, generic.Namespace, generic.Name, typeArguments.ToArray())
            : decoratorType;

    /// <summary>
    /// Replaces type parameters with the arguments the registration closed them over, at any depth.
    /// </summary>
    private static ITypeDefinition Substitute(
        ITypeDefinition type, Dictionary<string, ITypeDefinition> substitutions) {

        if (type is GenericTypeDefinition generic) {
            var arguments = new ITypeDefinition[generic.TypeArguments.Count];

            for (var i = 0; i < arguments.Length; i++) {
                arguments[i] = Substitute(generic.TypeArguments[i], substitutions);
            }

            return new GenericTypeDefinition(
                generic.TypeDefinitionEnum, generic.Namespace, generic.Name, arguments);
        }

        // A type parameter has no namespace; anything with one is an ordinary type and is left alone
        // even if it shares a name with a parameter.
        return string.IsNullOrEmpty(type.Namespace) && substitutions.TryGetValue(type.Name, out var closed)
            ? closed
            : type;
    }
}

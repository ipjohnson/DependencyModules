using System.Globalization;
using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads the members of an interface into the model a generated wrapper is written from.
/// </summary>
/// <remarks>
/// Everything a symbol contributes is converted here, during the transform: a symbol is not
/// equatable across compilations, so holding one would defeat the incremental cache and regenerate
/// every wrapper on every keystroke.
/// </remarks>
public static class InterceptedMemberReader {

    /// <summary>
    /// Everything a wrapper for <paramref name="serviceType"/> has to implement, including what it
    /// inherits from base interfaces.
    /// </summary>
    /// <returns>
    /// False when the interface contains something that cannot be forwarded, so the caller reports
    /// it rather than emitting a wrapper that does not compile.
    /// </returns>
    public static bool Read(
        INamedTypeSymbol serviceType,
        out IReadOnlyList<InterceptedMemberModel> members,
        out IReadOnlyList<InterceptedDeclarationModel> declarations,
        out string? unsupported) {

        unsupported = null;

        var memberList = new List<InterceptedMemberModel>();
        var declarationList = new List<InterceptedDeclarationModel>();

        members = memberList;
        declarations = declarationList;

        foreach (var candidate in EnumerateMembers(serviceType)) {
            if (candidate.IsStatic) {
                unsupported = $"'{candidate.Name}' is static, which cannot be forwarded through an instance";
                return false;
            }

            switch (candidate) {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    if (!ReadMethod(method, memberList, declarationList, out unsupported)) {
                        return false;
                    }
                    break;

                case IPropertySymbol property:
                    if (!ReadProperty(property, memberList, declarationList, out unsupported)) {
                        return false;
                    }
                    break;

                case IEventSymbol @event:
                    if (!ReadEvent(@event, memberList, declarationList, out unsupported)) {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    private static bool ReadMethod(
        IMethodSymbol method,
        List<InterceptedMemberModel> members,
        List<InterceptedDeclarationModel> declarations,
        out string? unsupported) {

        if (method.ReturnsByRef || method.ReturnsByRefReadonly) {
            unsupported = $"'{method.Name}' returns by reference, which cannot be wrapped";
            return false;
        }

        var parameters = ReadParameters(method.Parameters, method.Name, out unsupported);

        if (parameters == null) {
            return false;
        }

        var returnShape = GetReturnShape(method.ReturnType);

        declarations.Add(new InterceptedDeclarationModel(
            DeclarationKind.Method,
            EscapeIdentifier(method.Name),
            null,
            Array.Empty<InterceptedParameterModel>(),
            members.Count,
            -1));

        members.Add(new InterceptedMemberModel(
            method.Name,
            EscapeIdentifier(method.Name),
            AccessorForm.Method,
            returnShape == ReturnShape.Void ? null : method.ReturnType.GetTypeDefinition(),
            GetResultType(method.ReturnType, returnShape),
            parameters,
            ReadTypeParameters(method),
            returnShape));

        return true;
    }

    /// <summary>
    /// A property or indexer, as one declaration and one pipeline unit per accessor.
    /// </summary>
    /// <remarks>
    /// An accessor cannot be async whatever its type, so both take the sync path: a property of type
    /// <c>Task&lt;T&gt;</c> hands the task itself to the interceptor as its result, because that is
    /// what the getter returns.
    /// </remarks>
    private static bool ReadProperty(
        IPropertySymbol property,
        List<InterceptedMemberModel> members,
        List<InterceptedDeclarationModel> declarations,
        out string? unsupported) {

        if (property.ReturnsByRef || property.ReturnsByRefReadonly) {
            unsupported = $"'{property.Name}' returns by reference, which cannot be wrapped";
            return false;
        }

        if (property.SetMethod is { IsInitOnly: true }) {
            unsupported =
                $"'{property.Name}' is init-only, and a wrapper cannot forward to an initializer";
            return false;
        }

        var indices = ReadParameters(property.Parameters, property.Name, out unsupported);

        if (indices == null) {
            return false;
        }

        if (property.Type.IsRefLikeType) {
            unsupported = $"'{property.Name}' is a ref struct and cannot be held for the duration of a call";
            return false;
        }

        var type = property.Type.GetTypeDefinition();
        var getter = -1;
        var setter = -1;

        if (property.GetMethod != null) {
            getter = members.Count;

            members.Add(new InterceptedMemberModel(
                property.GetMethod.Name,
                EscapeIdentifier(property.Name),
                property.IsIndexer ? AccessorForm.IndexerGet : AccessorForm.PropertyGet,
                type,
                type,
                indices,
                Array.Empty<InterceptedTypeParameterModel>(),
                ReturnShape.Value));
        }

        if (property.SetMethod != null) {
            setter = members.Count;

            // The assigned value is the last argument, after any indices, matching how the CLR names
            // and orders a setter's parameters.
            var arguments = new List<InterceptedParameterModel>(indices) {
                new("value", "value", type, null)
            };

            members.Add(new InterceptedMemberModel(
                property.SetMethod.Name,
                EscapeIdentifier(property.Name),
                property.IsIndexer ? AccessorForm.IndexerSet : AccessorForm.PropertySet,
                null,
                KnownTypes.DependencyModules.Interception.NoResult,
                arguments,
                Array.Empty<InterceptedTypeParameterModel>(),
                ReturnShape.Void));
        }

        declarations.Add(new InterceptedDeclarationModel(
            property.IsIndexer ? DeclarationKind.Indexer : DeclarationKind.Property,
            property.IsIndexer ? "this" : EscapeIdentifier(property.Name),
            type,
            indices,
            getter,
            setter));

        return true;
    }

    private static bool ReadEvent(
        IEventSymbol @event,
        List<InterceptedMemberModel> members,
        List<InterceptedDeclarationModel> declarations,
        out string? unsupported) {

        unsupported = null;

        if (@event.AddMethod == null || @event.RemoveMethod == null) {
            unsupported = $"'{@event.Name}' does not declare both add and remove";
            return false;
        }

        var type = @event.Type.GetTypeDefinition();
        var add = members.Count;

        members.Add(EventAccessor(@event.AddMethod.Name, @event.Name, AccessorForm.EventAdd, type));

        var remove = members.Count;

        members.Add(EventAccessor(@event.RemoveMethod.Name, @event.Name, AccessorForm.EventRemove, type));

        declarations.Add(new InterceptedDeclarationModel(
            DeclarationKind.Event,
            EscapeIdentifier(@event.Name),
            type,
            Array.Empty<InterceptedParameterModel>(),
            add,
            remove));

        return true;
    }

    private static InterceptedMemberModel EventAccessor(
        string name, string eventName, AccessorForm form, ITypeDefinition handlerType) =>
        new(name,
            EscapeIdentifier(eventName),
            form,
            null,
            KnownTypes.DependencyModules.Interception.NoResult,
            new InterceptedParameterModel[] { new("value", "value", handlerType, null) },
            Array.Empty<InterceptedTypeParameterModel>(),
            ReturnShape.Void);

    /// <summary>
    /// The interface's own members plus everything it inherits. Skipping base interfaces would
    /// produce a wrapper that does not satisfy the interface.
    /// </summary>
    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol serviceType) {
        foreach (var member in serviceType.GetMembers()) {
            yield return member;
        }

        foreach (var baseInterface in serviceType.AllInterfaces) {
            foreach (var member in baseInterface.GetMembers()) {
                yield return member;
            }
        }
    }

    /// <summary>
    /// The parameters, as the fields an argument lives in for the duration of a call.
    /// </summary>
    /// <remarks>
    /// A parameter passed by reference cannot live in a field, and an async method cannot declare one
    /// at all, so the two shapes that arguments make impossible are refused here rather than emitted
    /// as code that does not compile. A hand-written decorator remains the answer for those.
    /// </remarks>
    private static IReadOnlyList<InterceptedParameterModel>? ReadParameters(
        IReadOnlyList<IParameterSymbol> declared, string memberName, out string? unsupported) {

        unsupported = null;

        var parameters = new List<InterceptedParameterModel>();

        foreach (var parameter in declared) {
            if (parameter.RefKind != RefKind.None) {
                var keyword = parameter.RefKind switch {
                    RefKind.Ref => "ref",
                    RefKind.Out => "out",
                    _ => "in"
                };

                unsupported =
                    $"'{memberName}' takes '{parameter.Name}' by {keyword}, and an argument passed by " +
                    "reference cannot be held for the duration of a call";

                return null;
            }

            if (parameter.Type.IsRefLikeType) {
                unsupported =
                    $"'{memberName}' takes '{parameter.Name}', which is a ref struct and cannot be " +
                    "held for the duration of a call";

                return null;
            }

            parameters.Add(new InterceptedParameterModel(
                parameter.Name,
                EscapeIdentifier(parameter.Name),
                parameter.Type.GetTypeDefinition(),
                RenderDefaultValue(parameter),
                parameter.IsParams));
        }

        return parameters;
    }

    /// <summary>
    /// A parameter's default as it should be written on the wrapper. Dropping it would narrow the
    /// signature the interface promised.
    /// </summary>
    private static string? RenderDefaultValue(IParameterSymbol parameter) {
        if (!parameter.HasExplicitDefaultValue) {
            return null;
        }

        var value = parameter.ExplicitDefaultValue;

        if (value == null) {
            return parameter.Type.IsValueType ? "default" : "null";
        }

        // An enum default arrives as its underlying value, so it is cast back rather than guessed at
        // by matching the value against the enum's members.
        if (parameter.Type.TypeKind == TypeKind.Enum) {
            var builder = new StringBuilder("(");

            parameter.Type.GetTypeDefinition().WriteTypeName(builder, TypeOutputMode.Global);

            return builder.Append(')')
                .Append(Convert.ToString(value, CultureInfo.InvariantCulture))
                .ToString();
        }

        return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(
            value, quoteStrings: true, useHexadecimalNumbers: false);
    }

    /// <summary>
    /// The member's type parameters and their constraints. The state class repeats both, or the call
    /// it forwards will not satisfy the constraints the interface declared.
    /// </summary>
    private static IReadOnlyList<InterceptedTypeParameterModel> ReadTypeParameters(IMethodSymbol method) {
        if (method.TypeParameters.Length == 0) {
            return Array.Empty<InterceptedTypeParameterModel>();
        }

        var typeParameters = new List<InterceptedTypeParameterModel>();

        foreach (var parameter in method.TypeParameters) {
            typeParameters.Add(new InterceptedTypeParameterModel(parameter.Name, RenderConstraints(parameter)));
        }

        return typeParameters;
    }

    private static string RenderConstraints(ITypeParameterSymbol parameter) {
        var constraints = new List<string>();

        // Unmanaged implies a value type constraint, so it has to be tested first or the narrower
        // constraint would be rendered as the wider one.
        if (parameter.HasUnmanagedTypeConstraint) {
            constraints.Add("unmanaged");
        } else if (parameter.HasValueTypeConstraint) {
            constraints.Add("struct");
        } else if (parameter.HasReferenceTypeConstraint) {
            constraints.Add(
                parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                    ? "class?"
                    : "class");
        } else if (parameter.HasNotNullConstraint) {
            constraints.Add("notnull");
        }

        foreach (var constraintType in parameter.ConstraintTypes) {
            var builder = new StringBuilder();

            constraintType.GetTypeDefinition().WriteTypeName(builder, TypeOutputMode.Global);

            constraints.Add(builder.ToString());
        }

        if (parameter.HasConstructorConstraint) {
            constraints.Add("new()");
        }

        return string.Join(", ", constraints);
    }

    private static string EscapeIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? name
            : "@" + name;

    private static ReturnShape GetReturnShape(ITypeSymbol returnType) {
        if (returnType.SpecialType == SpecialType.System_Void) {
            return ReturnShape.Void;
        }

        // Matched on the metadata name of the unbound type, so the match does not depend on what the
        // framework happens to have named the type parameter of Task<T>.
        var definition = returnType.OriginalDefinition;

        var name = definition.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString() + "." + definition.MetadataName
            : definition.MetadataName;

        return name switch {
            "System.Threading.Tasks.Task" => ReturnShape.Task,
            "System.Threading.Tasks.Task`1" => ReturnShape.TaskOfValue,
            "System.Threading.Tasks.ValueTask" => ReturnShape.ValueTask,
            "System.Threading.Tasks.ValueTask`1" => ReturnShape.ValueTaskOfValue,
            "System.Collections.Generic.IAsyncEnumerable`1" => ReturnShape.AsyncEnumerable,
            _ => ReturnShape.Value
        };
    }

    /// <summary>
    /// The type the invocation state is closed over. A member that produces nothing still flows
    /// through the same pipeline, standing on <c>NoResult</c> so an interceptor never needs an
    /// overload for the void case.
    /// </summary>
    private static ITypeDefinition GetResultType(ITypeSymbol returnType, ReturnShape shape) {
        switch (shape) {
            case ReturnShape.Void:
            case ReturnShape.Task:
            case ReturnShape.ValueTask:
                return KnownTypes.DependencyModules.Interception.NoResult;

            case ReturnShape.TaskOfValue:
            case ReturnShape.ValueTaskOfValue:
            case ReturnShape.AsyncEnumerable:
                return ((INamedTypeSymbol)returnType).TypeArguments[0].GetTypeDefinition();

            default:
                return returnType.GetTypeDefinition();
        }
    }
}

using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;
using Interception = DependencyModules.SourceGenerator.Impl.KnownTypes.DependencyModules.Interception;
using SystemTypes = DependencyModules.SourceGenerator.Impl.KnownTypes.System;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// Writes the wrapper type that routes each call to an intercepted service through its interceptors.
/// </summary>
/// <remarks>
/// The output is a pipeline rather than a set of hooks: each member builds a state object holding
/// its arguments, and the state dispatches one stage at a time, so an interceptor holds the whole
/// call in a single method body and any state it keeps is an ordinary local rather than a field
/// shared between concurrent calls.
///
/// The interceptors are typed fields rather than an injected collection. That is what keeps two
/// services from cross-applying each other's interceptors, and it lets the sync, async and stream
/// choice be settled here rather than at run time: the call emitted is a direct one to a known type.
///
/// Every generated name carries a <c>_dm</c> prefix. The wrapper implements a user's interface, and
/// a member of that interface named <c>_inner</c> would otherwise collide with the field.
/// </remarks>
public class InterceptorFileWriter {

    private const string InnerField = "_dmInner";

    public string Write(InterceptorModel model, string wrapperName, string namespaceName) {
        var csharpFile = new CSharpFileDefinition(namespaceName);

        var wrapper = csharpFile.AddClass(wrapperName);

        wrapper.Modifiers |= ComponentModifier.Internal;
        wrapper.AddBaseType(model.ServiceType);
        wrapper.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));

        WriteFields(wrapper, model);
        WriteConstructor(wrapper, model);

        foreach (var declaration in model.Declarations) {
            WriteDeclaration(wrapper, model, declaration);
        }

        for (var index = 0; index < model.Members.Count; index++) {
            WriteState(wrapper, model, model.Members[index], index, wrapperName);
        }

        // The indexer hands arguments back as object?, so the file needs an annotation context.
        csharpFile.EnableNullable();

        var output = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(output);

        return output.Output();
    }

    private static void WriteFields(ClassDefinition wrapper, InterceptorModel model) {
        var inner = wrapper.AddField(model.ServiceType, InnerField);
        inner.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;

        for (var index = 0; index < model.Interceptors.Count; index++) {
            var interceptor = wrapper.AddField(model.Interceptors[index].Type, InterceptorField(index));
            interceptor.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;
        }

        // Everything identifying a member is known now, so one caller is built per member and shared
        // by every call rather than constructed per invocation.
        for (var index = 0; index < model.Members.Count; index++) {
            var caller = wrapper.AddField(
                Interception.CallerInfo, CallerField(index));

            caller.Modifiers |= ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
            caller.InitializeValue = New(
                Interception.CallerInfo,
                TypeOf(model.ServiceType),
                QuoteString(model.Members[index].Name));
        }
    }

    private static void WriteConstructor(ClassDefinition wrapper, InterceptorModel model) {
        var constructor = wrapper.AddConstructor();

        constructor.AddParameter(model.ServiceType, "inner");
        constructor.AddIndentedStatement($"{InnerField} = inner");

        for (var index = 0; index < model.Interceptors.Count; index++) {
            constructor.AddParameter(model.Interceptors[index].Type, $"interceptor{index}");
            constructor.AddIndentedStatement($"{InterceptorField(index)} = interceptor{index}");
        }
    }

    /// <summary>
    /// One member as the interface declares it, with each accessor forwarding into the pipeline.
    /// </summary>
    private static void WriteDeclaration(
        ClassDefinition wrapper, InterceptorModel model, InterceptedDeclarationModel declaration) {

        switch (declaration.Kind) {
            case DeclarationKind.Method:
                WriteForwardingMethod(wrapper, model.Members[declaration.First], declaration.First);
                break;

            case DeclarationKind.Property:
                WriteProperty(wrapper, model, declaration);
                break;

            case DeclarationKind.Indexer:
                WriteAccessorMember(
                    wrapper, model, declaration, AccessorMemberDefinition.Indexer(declaration.Type!));
                break;

            case DeclarationKind.Event:
                WriteAccessorMember(
                    wrapper,
                    model,
                    declaration,
                    AccessorMemberDefinition.Event(declaration.Type!, declaration.Identifier));
                break;
        }
    }

    private static void WriteProperty(
        ClassDefinition wrapper, InterceptorModel model, InterceptedDeclarationModel declaration) {

        var property = wrapper.AddProperty(declaration.Type!, declaration.Identifier);

        property.Modifiers |= ComponentModifier.Public;

        if (declaration.First >= 0) {
            WriteAccessorBody(property.Get, model.Members[declaration.First], declaration.First);
        }

        if (declaration.Second < 0) {
            // A get-only property. Leaving the setter in place would declare one the interface does
            // not have, and PropertyDefinition writes an empty pair as an auto-property.
            property.Set = null;
        } else {
            WriteAccessorBody(property.Set!, model.Members[declaration.Second], declaration.Second);
        }
    }

    private static void WriteAccessorMember(
        ClassDefinition wrapper,
        InterceptorModel model,
        InterceptedDeclarationModel declaration,
        AccessorMemberDefinition member) {

        member.Modifiers |= ComponentModifier.Public;

        foreach (var index in declaration.Indices) {
            member.Parameters.Add(new ParameterDefinition(index.Type, index.Identifier));
        }

        if (declaration.First >= 0) {
            member.First = new PropertyMethodDefinition();

            WriteAccessorBody(member.First, model.Members[declaration.First], declaration.First);
        }

        if (declaration.Second >= 0) {
            member.Second = new PropertyMethodDefinition();

            WriteAccessorBody(member.Second, model.Members[declaration.Second], declaration.Second);
        }

        wrapper.AddComponent(member);
    }

    /// <summary>
    /// An accessor body. The arguments come straight off the member, which already holds them in the
    /// order the CLR gives an accessor: any indices, then the assigned value.
    /// </summary>
    private static void WriteAccessorBody(
        PropertyMethodDefinition accessor, InterceptedMemberModel member, int index) {

        var arguments = new List<string> { "this" };

        arguments.AddRange(member.Parameters.Select(parameter => parameter.Identifier));

        accessor.AddIndentedStatement(
            $"var state = new {StateName(member, index)}({string.Join(", ", arguments)})");

        accessor.NewLine();

        if (member.ReturnShape == ReturnShape.Void) {
            accessor.AddIndentedStatement("state.Invoke(0)");
        } else {
            accessor.Return("state.Invoke(0)");
        }
    }

    /// <summary>
    /// The method as the interface declares it, forwarding into the pipeline.
    /// </summary>
    private static void WriteForwardingMethod(
        ClassDefinition wrapper, InterceptedMemberModel member, int index) {

        var method = wrapper.AddMethod(member.Identifier);

        method.Modifiers |= ComponentModifier.Public;

        if (member.ReturnType != null) {
            method.SetReturnType(member.ReturnType);
        }

        foreach (var typeParameter in member.TypeParameters) {
            method.AddGenericParameter(TypeParameter(typeParameter.Name));
        }

        var constraints = RenderConstraints(member);

        if (constraints != null) {
            method.WhereStatement = new CodeOutputComponent(" " + constraints) { Indented = false };
        }

        var arguments = new List<string> { "this" };

        foreach (var parameter in member.Parameters) {
            var declared = method.AddParameter(parameter.Type, parameter.Identifier);

            if (parameter.DefaultValue != null) {
                declared.DefaultValue = new CodeOutputComponent(parameter.DefaultValue) { Indented = false };
            }

            arguments.Add(parameter.Identifier);
        }

        // A ValueTask cannot be built from the pipeline's ValueTask<NoResult> without either an await
        // or an allocation, so this one shape is written as an async method.
        if (member.ReturnShape == ReturnShape.ValueTask) {
            method.Modifiers |= ComponentModifier.Async;
        }

        method.AddIndentedStatement(
            $"var state = new {StateName(member, index)}({string.Join(", ", arguments)})");

        method.NewLine();

        switch (member.ReturnShape) {
            case ReturnShape.Void:
                method.AddIndentedStatement("state.Invoke(0)");
                break;

            case ReturnShape.Task:
            case ReturnShape.TaskOfValue:
                method.Return("state.Invoke(0).AsTask()");
                break;

            case ReturnShape.ValueTask:
                method.AddIndentedStatement("await state.Invoke(0)");
                break;

            default:
                method.Return("state.Invoke(0)");
                break;
        }
    }

    /// <summary>
    /// The per-call state: the arguments as typed fields, and the stage dispatch that walks the
    /// pipeline.
    /// </summary>
    /// <remarks>
    /// Numbered rather than named after its member, because overloads would collide. Its type
    /// parameters repeat the member's, since a nested type cannot close over a method's.
    /// </remarks>
    private static void WriteState(
        ClassDefinition wrapper,
        InterceptorModel model,
        InterceptedMemberModel member,
        int index,
        string wrapperName) {

        var baseType = member.Kind switch {
            InterceptorKind.Async => Interception.AsyncInvocationState(member.ResultType),
            InterceptorKind.Stream => Interception.StreamInvocationState(member.ResultType),
            _ => Interception.InvocationState(member.ResultType)
        };

        var constraints = RenderConstraints(member);

        var state = wrapper.AddClass(StateName(member, index));

        state.Modifiers |= ComponentModifier.Private;
        state.AddBaseType(constraints == null ? baseType : new ConstrainedTypeDefinition(baseType, constraints));

        WriteStateFields(state, member, wrapperName);
        WriteStateConstructor(state, member, index, wrapperName);
        WriteCallerAndCount(state, member, index);
        WriteIndexer(state, member);
        WriteNameAt(state, member);
        WriteInvoke(state, model, member, wrapperName);
    }

    private static void WriteStateFields(
        ClassDefinition state, InterceptedMemberModel member, string wrapperName) {

        var self = state.AddField(TypeDefinition.Get("", wrapperName), "_self");
        self.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;

        for (var index = 0; index < member.Parameters.Count; index++) {
            var argument = state.AddField(member.Parameters[index].Type, ArgumentField(index));
            argument.Modifiers |= ComponentModifier.Private;
        }
    }

    /// <summary>
    /// Assigned through a constructor rather than an object initializer, so a field holding a
    /// non-nullable reference is definitely assigned and the wrapper needs no nullable suppression.
    /// </summary>
    private static void WriteStateConstructor(
        ClassDefinition state, InterceptedMemberModel member, int index, string wrapperName) {

        // Named without the type parameters: a constructor is DmState0, while the class it belongs
        // to is DmState0<T>. AddConstructor would take the class name whole.
        var constructor = new ConstructorDefinition($"DmState{index}");

        state.AddComponent(constructor);

        constructor.AddParameter(TypeDefinition.Get("", wrapperName), "self");
        constructor.AddIndentedStatement("_self = self");

        for (var argument = 0; argument < member.Parameters.Count; argument++) {
            constructor.AddParameter(member.Parameters[argument].Type, $"arg{argument}");
            constructor.AddIndentedStatement($"{ArgumentField(argument)} = arg{argument}");
        }
    }

    private static void WriteCallerAndCount(ClassDefinition state, InterceptedMemberModel member, int index) {
        var caller = state.AddProperty(Interception.CallerInfo, "Caller");

        caller.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        caller.Set = null;
        caller.Get.LambdaSyntax = true;
        caller.Get.AddCode(CallerField(index));

        var count = state.AddProperty(TypeDefinition.Get(typeof(int)), "Count");

        count.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        count.Set = null;
        count.Get.LambdaSyntax = true;
        count.Get.AddCode(member.Parameters.Count.ToString());
    }

    /// <summary>
    /// Reading boxes, and writing replaces the field the last stage passes on, so an interceptor
    /// that ignores the arguments pays for neither.
    /// </summary>
    private static void WriteIndexer(ClassDefinition state, InterceptedMemberModel member) {
        var indexer = state.AddProperty(TypeDefinition.Get(typeof(object)).MakeNullable(), "this");

        indexer.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        indexer.IndexType = TypeDefinition.Get(typeof(int));
        indexer.IndexName = "index";

        var get = indexer.Get.Switch("index");

        for (var index = 0; index < member.Parameters.Count; index++) {
            get.AddCase(index).Return(ArgumentField(index));
        }

        get.AddDefault().Throw(SystemTypes.ArgumentOutOfRangeException, "nameof(index)");

        var set = indexer.Set!.Switch("index");

        for (var index = 0; index < member.Parameters.Count; index++) {
            var block = set.AddCase(index);

            block.Assign(Bang(StaticCast(member.Parameters[index].Type, "value")))
                .To(ArgumentField(index));
            block.Break();
        }

        set.AddDefault().Throw(SystemTypes.ArgumentOutOfRangeException, "nameof(index)");
    }

    private static void WriteNameAt(ClassDefinition state, InterceptedMemberModel member) {
        var nameAt = state.AddMethod("NameAt");

        nameAt.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        nameAt.SetReturnType(typeof(string));
        nameAt.AddParameter(typeof(int), "index");

        var switchBlock = nameAt.Switch("index");

        for (var index = 0; index < member.Parameters.Count; index++) {
            switchBlock.AddCase(index).Return(QuoteString(member.Parameters[index].Name));
        }

        switchBlock.AddDefault().Throw(SystemTypes.ArgumentOutOfRangeException, "nameof(index)");
    }

    /// <summary>
    /// One stage per interceptor, then the implementation. The stage index arrives from the context
    /// rather than being held here, which is what lets an interceptor proceed more than once.
    /// </summary>
    private static void WriteInvoke(
        ClassDefinition state, InterceptorModel model, InterceptedMemberModel member, string wrapperName) {

        var (returnType, contextType, interceptMethod) = member.Kind switch {
            InterceptorKind.Async => (
                SystemTypes.ValueTask(member.ResultType),
                Interception.AsyncInvocationContext(member.ResultType),
                "InterceptAsync"),
            InterceptorKind.Stream => (
                SystemTypes.AsyncEnumerable(member.ResultType),
                Interception.StreamInvocationContext(member.ResultType),
                "InterceptStream"),
            _ => (
                member.ResultType,
                Interception.InvocationContext(member.ResultType),
                "Intercept")
        };

        var invoke = state.AddMethod("Invoke");

        invoke.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        invoke.SetReturnType(returnType);
        invoke.AddParameter(typeof(int), "stage");

        var switchBlock = invoke.Switch("stage");

        for (var index = 0; index < model.Interceptors.Count; index++) {
            switchBlock.AddCase(index).Return(
                CodeOutputComponent.Get($"_self.{InterceptorField(index)}")
                    .Invoke(interceptMethod, New(contextType, "this", index)));
        }

        var last = switchBlock.AddDefault();

        switch (member.ReturnShape) {
            case ReturnShape.Void:
                last.AddIndentedStatement(InnerCall(member));
                last.NewLine();
                last.Return("default");
                break;

            case ReturnShape.Task:
            case ReturnShape.ValueTask:
                last.Return("DmInvokeInner()");
                break;

            case ReturnShape.TaskOfValue:
                last.Return(New(SystemTypes.ValueTask(member.ResultType), InnerCall(member)));
                break;

            default:
                last.Return(InnerCall(member));
                break;
        }

        // A task with no result has to be awaited to be turned into a NoResult, and an await needs a
        // method to sit in. Left un-configured so the interceptor's continuation resumes on the
        // context it started on, the way a hand-written decorator would.
        if (member.ReturnShape is ReturnShape.Task or ReturnShape.ValueTask) {
            var inner = state.AddMethod("DmInvokeInner");

            inner.Modifiers |= ComponentModifier.Private | ComponentModifier.Async;
            inner.SetReturnType(SystemTypes.ValueTask(member.ResultType));
            inner.AddIndentedStatement($"await {InnerCall(member)}");
            inner.NewLine();
            inner.Return("default");
        }
    }

    /// <summary>
    /// The last stage: the call to the implementation, written the way the member is reached.
    /// </summary>
    /// <remarks>
    /// An accessor cannot be called by the name the CLR gives it. A getter is read, a setter and an
    /// event accessor are assignments, and an indexer is indexed, so each is written as its syntax
    /// rather than as <c>get_Count()</c>.
    /// </remarks>
    private static string InnerCall(InterceptedMemberModel member) {
        var target = $"_self.{InnerField}";
        var last = member.Parameters.Count - 1;

        switch (member.Form) {
            case AccessorForm.PropertyGet:
                return $"{target}.{member.Identifier}";

            case AccessorForm.PropertySet:
                return $"{target}.{member.Identifier} = {ArgumentField(last)}";

            case AccessorForm.IndexerGet:
                return $"{target}[{ArgumentFields(0, member.Parameters.Count)}]";

            // The assigned value is the last argument, and everything before it indexes.
            case AccessorForm.IndexerSet:
                return $"{target}[{ArgumentFields(0, last)}] = {ArgumentField(last)}";

            case AccessorForm.EventAdd:
                return $"{target}.{member.Identifier} += {ArgumentField(0)}";

            case AccessorForm.EventRemove:
                return $"{target}.{member.Identifier} -= {ArgumentField(0)}";

            default:
                var typeArguments = member.TypeParameters.Count == 0
                    ? ""
                    : "<" + string.Join(", ", member.TypeParameters.Select(parameter => parameter.Name)) + ">";

                return $"{target}.{member.Identifier}{typeArguments}" +
                       $"({ArgumentFields(0, member.Parameters.Count)})";
        }
    }

    private static string ArgumentFields(int start, int end) {
        var arguments = new List<string>();

        for (var index = start; index < end; index++) {
            arguments.Add(ArgumentField(index));
        }

        return string.Join(", ", arguments);
    }

    /// <summary>
    /// The state class repeats the member's type parameters, since a nested type cannot close over a
    /// method's.
    /// </summary>
    private static string StateName(InterceptedMemberModel member, int index) {
        if (member.TypeParameters.Count == 0) {
            return $"DmState{index}";
        }

        return $"DmState{index}<" +
               string.Join(", ", member.TypeParameters.Select(parameter => parameter.Name)) +
               ">";
    }

    private static string? RenderConstraints(InterceptedMemberModel member) {
        var clauses = member.TypeParameters
            .Where(parameter => parameter.Constraints.Length > 0)
            .Select(parameter => $"where {parameter.Name} : {parameter.Constraints}")
            .ToList();

        return clauses.Count == 0 ? null : string.Join(" ", clauses);
    }

    private static ITypeDefinition TypeParameter(string name) =>
        new TypeParameterDefinition(TypeDefinitionEnum.ClassDefinition, false, false, name);

    private static string InterceptorField(int index) => $"_dmInterceptor{index}";

    private static string CallerField(int index) => $"_dmCaller{index}";

    private static string ArgumentField(int index) => $"_arg{index}";
}

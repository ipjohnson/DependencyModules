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

        // A generic service is wrapped by a generic type closed over the same parameters, which is
        // what lets the container register it as an open generic implementation. Only constraint-free
        // parameters reach here; a constrained one is refused upstream, because the wrapper would have
        // to repeat the constraint and there is no way to emit one.
        if (model.IsOpenGeneric) {
            foreach (var typeParameter in model.TypeParameters!) {
                wrapper.AddGenericParameter(typeParameter.Name);

                WriteConstraint(wrapper.AddConstraint(typeParameter.Name), typeParameter);
            }
        }

        wrapper.AddBaseType(model.ServiceType);
        wrapper.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));

        WriteFields(wrapper, model);
        WriteConstructor(wrapper, model);

        foreach (var declaration in model.Declarations) {
            WriteDeclaration(wrapper, model, declaration);
        }

        for (var index = 0; index < model.Members.Count; index++) {
            if (IsIntercepted(model, model.Members[index])) {
                WriteState(wrapper, model, model.Members[index], index, wrapperName);
            }
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

    /// <summary>
    /// What the wrapper holds and is handed as the instance it wraps.
    /// </summary>
    /// <remarks>
    /// The service interface for an ordinary wrapper, which the container hands over because
    /// decoration captured the original registration first.
    ///
    /// For an open generic there is no factory to capture anything, so the wrapper <i>is</i> the
    /// registration for the service — and asking for the service would resolve the wrapper itself and
    /// recurse. It takes the implementation by its own concrete type instead, which
    /// <c>DecoratorHelper.InterceptOpenGeneric</c> registers alongside it.
    /// </remarks>
    private static ITypeDefinition InnerType(InterceptorModel model) =>
        model.IsOpenGeneric ? Closed(model.ImplementationType, model.TypeParameters!) : model.ServiceType;

    /// <summary>
    /// A type closed over the wrapper's own type parameters — <c>Repository</c> becomes
    /// <c>Repository&lt;T&gt;</c>.
    /// </summary>
    private static ITypeDefinition Closed(
        ITypeDefinition type, IReadOnlyList<TypeParameterModel> typeParameters) {

        var arguments = new ITypeDefinition[typeParameters.Count];

        for (var i = 0; i < arguments.Length; i++) {
            arguments[i] = TypeDefinition.Get("", typeParameters[i].Name);
        }

        return new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, type.Namespace, type.Name, arguments);
    }

    /// <summary>
    /// How a nested state class names the wrapper that owns it.
    /// </summary>
    /// <remarks>
    /// A nested type inherits its outer type's parameters but still has to write them: inside
    /// <c>Repository_Intercepted&lt;T&gt;</c> the name is <c>Repository_Intercepted&lt;T&gt;</c>, and
    /// the bare name is CS0305.
    /// </remarks>
    private static ITypeDefinition SelfType(InterceptorModel model, string wrapperName) =>
        model.IsOpenGeneric
            ? Closed(TypeDefinition.Get("", wrapperName), model.TypeParameters!)
            : TypeDefinition.Get("", wrapperName);

    /// <summary>
    /// The constraints a member declares, which both the forwarding member and its state class have
    /// to repeat or the call they forward will not satisfy them.
    /// </summary>
    private static void WriteConstraints(
        InterceptedMemberModel member, Func<string, ConstraintDefinition> addConstraint) {

        foreach (var typeParameter in member.TypeParameters) {
            WriteConstraint(addConstraint(typeParameter.Name), typeParameter);
        }
    }

    /// <summary>
    /// Repeats one type parameter's constraints.
    /// </summary>
    /// <remarks>
    /// The parts go in as the symbol reported them and come out in the order C# requires, which is
    /// <c>ConstraintDefinition</c>'s job rather than this writer's.
    /// </remarks>
    private static void WriteConstraint(ConstraintDefinition constraint, TypeParameterModel typeParameter) {
        switch (typeParameter.Primary) {
            case "class":
                constraint.Class();
                break;
            case "class?":
                constraint.Class(nullable: true);
                break;
            case "struct":
                constraint.Struct();
                break;
            case "unmanaged":
                constraint.Unmanaged();
                break;
            case "notnull":
                constraint.NotNull();
                break;
        }

        foreach (var constraintType in typeParameter.ConstraintTypes) {
            constraint.Implements(constraintType);
        }

        if (typeParameter.DefaultConstructor) {
            constraint.DefaultConstructor();
        }
    }

    private static void WriteFields(ClassDefinition wrapper, InterceptorModel model) {
        var inner = wrapper.AddField(InnerType(model), InnerField);
        inner.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;

        for (var index = 0; index < model.Interceptors.Count; index++) {
            var interceptor = wrapper.AddField(model.Interceptors[index].Type, InterceptorField(index));
            interceptor.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;
        }

        // Everything identifying a member is known now, so one caller is built per member and shared
        // by every call rather than constructed per invocation. A member nothing intercepts has no
        // caller, because it has no pipeline to report itself to.
        for (var index = 0; index < model.Members.Count; index++) {
            if (!IsIntercepted(model, model.Members[index])) {
                continue;
            }

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

        constructor.AddParameter(InnerType(model), "inner");
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
                WriteForwardingMethod(wrapper, model, model.Members[declaration.First], declaration.First);
                break;

            case DeclarationKind.Property:
                WriteProperty(wrapper, model, declaration);
                break;

            case DeclarationKind.Indexer:
                WriteIndexer(wrapper, model, declaration);
                break;

            case DeclarationKind.Event:
                WriteEvent(wrapper, model, declaration);
                break;
        }
    }

    private static void WriteProperty(
        ClassDefinition wrapper, InterceptorModel model, InterceptedDeclarationModel declaration) {

        var property = wrapper.AddProperty(declaration.Type!, declaration.Identifier);

        property.Modifiers |= ComponentModifier.Public;

        if (declaration.First >= 0) {
            WriteAccessorBody(property.Get, model, model.Members[declaration.First], declaration.First);
        }

        if (declaration.Second < 0) {
            // A get-only property. Leaving the setter in place would declare one the interface does
            // not have, and PropertyDefinition writes an empty pair as an auto-property.
            property.Set = null;
        } else {
            WriteAccessorBody(property.Set!, model, model.Members[declaration.Second], declaration.Second);
        }
    }

    private static void WriteIndexer(
        ClassDefinition wrapper, InterceptorModel model, InterceptedDeclarationModel declaration) {

        var indexer = wrapper.AddProperty(declaration.Type!, "this");

        indexer.Modifiers |= ComponentModifier.Public;

        foreach (var index in declaration.Indices) {
            indexer.AddIndexParameter(index.Type, index.Identifier);
        }

        if (declaration.First >= 0) {
            WriteAccessorBody(indexer.Get, model, model.Members[declaration.First], declaration.First);
        }

        if (declaration.Second < 0) {
            indexer.Set = null;
        } else {
            WriteAccessorBody(indexer.Set!, model, model.Members[declaration.Second], declaration.Second);
        }
    }

    private static void WriteEvent(
        ClassDefinition wrapper, InterceptorModel model, InterceptedDeclarationModel declaration) {

        var declared = wrapper.AddEvent(declaration.Type!, declaration.Identifier);

        declared.Modifiers |= ComponentModifier.Public;

        WriteAccessorBody(declared.Add, model, model.Members[declaration.First], declaration.First);
        WriteAccessorBody(declared.Remove, model, model.Members[declaration.Second], declaration.Second);
    }

    /// <summary>
    /// An accessor body. The arguments come straight off the member, which already holds them in the
    /// order the CLR gives an accessor: any indices, then the assigned value.
    /// </summary>
    private static void WriteAccessorBody(
        PropertyMethodDefinition accessor, InterceptorModel model, InterceptedMemberModel member, int index) {

        if (!IsIntercepted(model, member)) {
            WritePassThrough(accessor, member);

            return;
        }

        var arguments = new List<string> { "this" };

        arguments.AddRange(member.Parameters.Select(parameter => parameter.Identifier));

        accessor.AddIndentedStatement(
            $"var state = new {ClosedStateName(member, index)}({string.Join(", ", arguments)})");

        accessor.NewLine();

        if (member.ReturnShape == ReturnShape.Void) {
            accessor.AddIndentedStatement("state.Invoke(0)");
        } else {
            accessor.Return("state.Invoke(0)");
        }
    }

    /// <summary>
    /// A member no interceptor serves, forwarded straight to the implementation.
    /// </summary>
    /// <remarks>
    /// An interface is intercepted as a whole, and one that mixes synchronous and asynchronous
    /// members is the normal case rather than a mistake — an interceptor implements the interfaces
    /// it can serve and has nothing to say about the rest. Those members build no state and
    /// allocate nothing.
    /// </remarks>
    private static void WritePassThrough(BaseBlockDefinition block, InterceptedMemberModel member) {
        var call = InnerCall(member, InnerField);

        if (member.ReturnShape == ReturnShape.Void) {
            block.AddIndentedStatement(call);
        } else {
            block.Return(call);
        }
    }

    /// <summary>
    /// The method as the interface declares it, forwarding into the pipeline.
    /// </summary>
    private static void WriteForwardingMethod(
        ClassDefinition wrapper, InterceptorModel model, InterceptedMemberModel member, int index) {

        var method = wrapper.AddMethod(member.Identifier);

        method.Modifiers |= ComponentModifier.Public;

        if (member.ReturnType != null) {
            method.SetReturnType(member.ReturnType);
        }

        foreach (var typeParameter in member.TypeParameters) {
            method.AddGenericParameter(new TypeParameterDefinition(typeParameter.Name));
        }

        WriteConstraints(member, method.AddConstraint);

        var arguments = new List<string> { "this" };

        foreach (var parameter in member.Parameters) {
            var declared = method.AddParameter(parameter.Type, parameter.Identifier);

            // Dropping params does not merely lose sugar: an optional parameter ahead of it becomes
            // an optional parameter followed by a required one, which the compiler refuses.
            declared.IsParams = parameter.IsParams;

            if (parameter.DefaultValue != null) {
                declared.DefaultValue = new CodeOutputComponent(parameter.DefaultValue) { Indented = false };
            }

            arguments.Add(parameter.Identifier);
        }

        if (!IsIntercepted(model, member)) {
            WritePassThrough(method, member);

            return;
        }

        // A ValueTask cannot be built from the pipeline's ValueTask<NoResult> without either an await
        // or an allocation, so this one shape is written as an async method.
        if (member.ReturnShape == ReturnShape.ValueTask) {
            method.Modifiers |= ComponentModifier.Async;
        }

        method.AddIndentedStatement(
            $"var state = new {ClosedStateName(member, index)}({string.Join(", ", arguments)})");

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

        var state = wrapper.AddClass(StateName(index));

        state.Modifiers |= ComponentModifier.Private | ComponentModifier.Sealed;
        state.AddBaseType(baseType);

        foreach (var typeParameter in member.TypeParameters) {
            state.AddGenericParameter(typeParameter.Name);
        }

        WriteConstraints(member, state.AddConstraint);

        var selfType = SelfType(model, wrapperName);

        WriteStateFields(state, member, selfType);
        WriteStateConstructor(state, member, index, selfType);
        WriteCallerAndCount(state, member, index);
        WriteArgumentsIndexer(state, member);
        WriteNameAt(state, member);
        WriteInvoke(state, model, member, wrapperName);
    }

    private static void WriteStateFields(
        ClassDefinition state, InterceptedMemberModel member, ITypeDefinition selfType) {

        var self = state.AddField(selfType, "_self");
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
        ClassDefinition state, InterceptedMemberModel member, int index, ITypeDefinition selfType) {

        var constructor = state.AddConstructor();

        constructor.AddParameter(selfType, "self");
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
    private static void WriteArgumentsIndexer(ClassDefinition state, InterceptedMemberModel member) {
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

        // The stage is this member's position in its own pipeline, which is not the interceptor's
        // position in the attribute: an interceptor that cannot serve this member is not a stage of
        // it. Proceed() walks stage + 1, so the numbering has to be contiguous.
        var stage = 0;

        for (var index = 0; index < model.Interceptors.Count; index++) {
            if (!model.Interceptors[index].CanServe(member.Kind)) {
                continue;
            }

            switchBlock.AddCase(stage).Return(
                CodeOutputComponent.Get($"_self.{InterceptorField(index)}")
                    .Invoke(interceptMethod, New(contextType, "this", stage)));

            stage++;
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
    /// <summary>
    /// The call to the implementation, written the way the member is reached.
    /// </summary>
    /// <remarks>
    /// An accessor cannot be called by the name the CLR gives it. A getter is read, a setter and an
    /// event accessor are assignments, and an indexer is indexed, so each is written as its syntax
    /// rather than as <c>get_Count()</c>.
    ///
    /// The last stage of a pipeline reads the arguments off the state; a member nothing intercepts
    /// has no state and passes on the parameters it was handed.
    /// </remarks>
    private static string InnerCall(InterceptedMemberModel member, string? passThroughTarget = null) {
        var target = passThroughTarget ?? $"_self.{InnerField}";
        var last = member.Parameters.Count - 1;

        string Argument(int index) =>
            passThroughTarget == null ? ArgumentField(index) : member.Parameters[index].Identifier;

        string Arguments(int start, int end) {
            var arguments = new List<string>();

            for (var index = start; index < end; index++) {
                arguments.Add(Argument(index));
            }

            return string.Join(", ", arguments);
        }

        switch (member.Form) {
            case AccessorForm.PropertyGet:
                return $"{target}.{member.Identifier}";

            case AccessorForm.PropertySet:
                return $"{target}.{member.Identifier} = {Argument(last)}";

            case AccessorForm.IndexerGet:
                return $"{target}[{Arguments(0, member.Parameters.Count)}]";

            // The assigned value is the last argument, and everything before it indexes.
            case AccessorForm.IndexerSet:
                return $"{target}[{Arguments(0, last)}] = {Argument(last)}";

            case AccessorForm.EventAdd:
                return $"{target}.{member.Identifier} += {Argument(0)}";

            case AccessorForm.EventRemove:
                return $"{target}.{member.Identifier} -= {Argument(0)}";

            default:
                var typeArguments = member.TypeParameters.Count == 0
                    ? ""
                    : "<" + string.Join(", ", member.TypeParameters.Select(parameter => parameter.Name)) + ">";

                return $"{target}.{member.Identifier}{typeArguments}" +
                       $"({Arguments(0, member.Parameters.Count)})";
        }
    }

    /// <summary>
    /// Whether any of the service's interceptors can be placed around this member.
    /// </summary>
    private static bool IsIntercepted(InterceptorModel model, InterceptedMemberModel member) {
        // Left out by [Intercept].Members. Still forwarded below, just not through the chain.
        if (member.Excluded) {
            return false;
        }

        foreach (var interceptor in model.Interceptors) {
            if (interceptor.CanServe(member.Kind)) {
                return true;
            }
        }

        return false;
    }

    private static string StateName(int index) => $"DmState{index}";

    /// <summary>
    /// The state class is constructed closed over the member's type parameters, since a nested type
    /// cannot close over a method's.
    /// </summary>
    private static string ClosedStateName(InterceptedMemberModel member, int index) {
        if (member.TypeParameters.Count == 0) {
            return StateName(index);
        }

        return $"{StateName(index)}<" +
               string.Join(", ", member.TypeParameters.Select(parameter => parameter.Name)) +
               ">";
    }


    private static string InterceptorField(int index) => $"_dmInterceptor{index}";

    private static string CallerField(int index) => $"_dmCaller{index}";

    private static string ArgumentField(int index) => $"_arg{index}";
}

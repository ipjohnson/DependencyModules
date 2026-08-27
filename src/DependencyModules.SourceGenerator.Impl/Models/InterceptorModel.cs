using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// How an intercepted member returns, which decides the shape of the generated wrapper body.
/// </summary>
/// <remarks>
/// Getting this wrong is the classic interception bug: reporting completion when a task is handed
/// back rather than when it finishes, so every duration measures nothing and every fault is missed.
/// The return type is known at compile time, so the right shape is emitted per member instead of
/// being sniffed at run time.
/// </remarks>
public enum ReturnShape {
    Void,
    Value,
    Task,
    TaskOfValue,
    ValueTask,
    ValueTaskOfValue,
    AsyncEnumerable
}

/// <summary>
/// Which of the three interceptor interfaces a member has to be routed through.
/// </summary>
/// <remarks>
/// A sync interceptor holds no way to await, so it cannot serve a member returning a task; an async
/// one has nowhere to await inside a sync member. A stream is not awaitable at all, and wrapping it
/// as a plain value would time the construction of the iterator rather than the work.
/// </remarks>
public enum InterceptorKind {
    Sync,
    Async,
    Stream
}

/// <summary>
/// A refusal to generate, carried on the model because a diagnostic cannot be reported from the
/// syntax transform — only the output stage holds the context that can report one.
/// </summary>
public record InterceptionRefusal(string Message);

/// <summary>
/// How the last stage reaches the implementation.
/// </summary>
/// <remarks>
/// An accessor is invoked by its syntax, not by its name: the CLR calls it <c>get_Count</c> and
/// reports it that way, but the call has to be written as <c>Count</c>.
/// </remarks>
public enum AccessorForm {
    Method,
    PropertyGet,
    PropertySet,
    IndexerGet,
    IndexerSet,
    EventAdd,
    EventRemove
}

/// <summary>
/// One parameter of an intercepted member.
/// </summary>
/// <param name="Name">
/// The declared name, which is what <c>NameAt</c> reports to an interceptor.
/// </param>
/// <param name="Identifier">
/// The name as it can be written in code, escaped when the declared name is a keyword.
/// </param>
/// <param name="Type">The parameter type.</param>
/// <param name="DefaultValue">
/// The default as it should be written, or null when the parameter is required. Dropping it would
/// change the signature callers see through the interface.
/// </param>
/// <param name="IsParams">
/// Whether the parameter was declared with <c>params</c>. Carried because dropping it can turn a
/// legal signature into an illegal one: <c>Join(string separator = ",", params string[] parts)</c>
/// becomes an optional parameter followed by a required one, which is CS1737 — in the generated
/// wrapper, for an interface that compiles perfectly well.
/// </param>
public record InterceptedParameterModel(
    string Name,
    string Identifier,
    ITypeDefinition Type,
    string? DefaultValue,
    bool IsParams = false);

/// <summary>
/// One type parameter of an intercepted member, with the constraints the wrapper has to repeat.
/// </summary>
/// <param name="Name">The type parameter as written, such as <c>T</c>.</param>
/// <param name="Constraints">
/// The constraints without the <c>where T :</c> prefix, such as <c>class, new()</c>, or empty.
/// </param>
/// <summary>
/// A type parameter of the intercepted <i>class</i>, with its constraints held as parts rather than
/// rendered.
/// </summary>
/// <remarks>
/// The wrapper is declared over the same parameters and has to repeat their constraints, or it cannot
/// reference the implementation it wraps. Parts rather than a string because the writer decides how a
/// type name is written and the reader does not know: rendering here would bake one output mode into
/// the model, and <c>CSharpAuthor</c>'s <c>AddConstraint</c> takes the pieces and puts them in the
/// order C# requires.
/// </remarks>
/// <param name="Name">The parameter name, repeated verbatim on the wrapper.</param>
/// <param name="Primary">
/// The primary constraint keyword — <c>class</c>, <c>struct</c>, <c>unmanaged</c>, <c>notnull</c> —
/// or null when there is none. At most one is legal.
/// </param>
/// <param name="ConstraintTypes">Base class and interface constraints, in declaration order.</param>
/// <param name="DefaultConstructor">Whether <c>new()</c> was declared.</param>
public record TypeParameterModel(
    string Name,
    string? Primary,
    IReadOnlyList<ITypeDefinition> ConstraintTypes,
    bool DefaultConstructor) {

    /// <summary>
    /// Structural equality over the constraint types, which the compiler-generated version compares
    /// by reference — two identical models built on consecutive runs would never match, and the
    /// incremental cache would miss on every keystroke.
    /// </summary>
    public virtual bool Equals(TypeParameterModel? other) =>
        other is not null &&
        Name == other.Name &&
        Primary == other.Primary &&
        DefaultConstructor == other.DefaultConstructor &&
        ModelEquality.ListEquals(ConstraintTypes, other.ConstraintTypes);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();

            hash = hash * 31 + (Primary?.GetHashCode() ?? 0);
            hash = hash * 31 + DefaultConstructor.GetHashCode();
            hash = hash * 31 + ModelEquality.ListHashCode(ConstraintTypes);

            return hash;
        }
    }
}

/// <summary>
/// An interceptor named by the attribute, with the interfaces it implements.
/// </summary>
/// <remarks>
/// Read from the symbol during the transform and reduced to three flags, because holding the symbol
/// itself would defeat the incremental cache. Which interfaces a type implements is what decides
/// whether it can serve a given member, and that decision is made once here rather than per member.
/// </remarks>
public record InterceptorTypeModel(
    ITypeDefinition Type,
    bool Sync,
    bool Async,
    bool Stream,
    /// <summary>
    /// The lifetime this interceptor is registered with, from the [Intercept] that named it.
    /// </summary>
    ServiceLifestyle Lifestyle = ServiceLifestyle.Singleton) {

    /// <summary>
    /// Whether this interceptor can be placed around a member of the given kind.
    /// </summary>
    public bool CanServe(InterceptorKind kind) =>
        kind switch {
            InterceptorKind.Sync => Sync,
            InterceptorKind.Async => Async,
            InterceptorKind.Stream => Stream,
            _ => false
        };
}

/// <summary>
/// A member of an intercepted interface, modelled during the syntax transform.
/// </summary>
/// <remarks>
/// Types rather than symbols: a symbol is not equatable across compilations, so holding one would
/// defeat the incremental cache and regenerate every wrapper on every keystroke.
/// </remarks>
/// <param name="Name">The member name, as the caller info reports it.</param>
/// <param name="Identifier">
/// The name to write when forwarding: the method name, or the property or event name. Not the CLR
/// accessor name, which cannot be called directly.
/// </param>
/// <param name="Form">How the last stage reaches the implementation.</param>
/// <param name="ReturnType">The declared return type, or null when the member returns void.</param>
/// <param name="ResultType">
/// The type the invocation state is closed over: the member's result, the type a task produces, the
/// type a stream yields, or <c>NoResult</c> when there is nothing to return.
/// </param>
/// <param name="Parameters">The parameters, which become the fields the arguments live in.</param>
/// <param name="TypeParameters">
/// The member's type parameters. The state class repeats them, since a nested type cannot close over
/// a method's type parameters.
/// </param>
/// <param name="ReturnShape">How the member returns.</param>
public record InterceptedMemberModel(
    string Name,
    string Identifier,
    AccessorForm Form,
    ITypeDefinition? ReturnType,
    ITypeDefinition ResultType,
    IReadOnlyList<InterceptedParameterModel> Parameters,
    IReadOnlyList<TypeParameterModel> TypeParameters,
    ReturnShape ReturnShape) {

    /// <summary>
    /// The interceptor interface this member has to be routed through.
    /// </summary>
    public InterceptorKind Kind =>
        ReturnShape switch {
            ReturnShape.Task or ReturnShape.TaskOfValue or
                ReturnShape.ValueTask or ReturnShape.ValueTaskOfValue => InterceptorKind.Async,
            ReturnShape.AsyncEnumerable => InterceptorKind.Stream,
            _ => InterceptorKind.Sync
        };

    /// <summary>
    /// Structural equality, because the compiler-generated version compares the two lists by
    /// reference and two identical models built on consecutive runs would never match.
    /// </summary>
    public virtual bool Equals(InterceptedMemberModel? other) {
        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other is null) {
            return false;
        }

        return Name == other.Name &&
               Identifier == other.Identifier &&
               Form == other.Form &&
               Equals(ReturnType, other.ReturnType) &&
               ResultType.Equals(other.ResultType) &&
               ReturnShape == other.ReturnShape &&
               ModelEquality.ListEquals(Parameters, other.Parameters) &&
               ModelEquality.ListEquals(TypeParameters, other.TypeParameters);
    }

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();

            hash = hash * 31 + Identifier.GetHashCode();
            hash = hash * 31 + (int)Form;
            hash = hash * 31 + (ReturnType?.GetHashCode() ?? 0);
            hash = hash * 31 + ResultType.GetHashCode();
            hash = hash * 31 + (int)ReturnShape;
            hash = hash * 31 + ModelEquality.ListHashCode(Parameters);
            hash = hash * 31 + ModelEquality.ListHashCode(TypeParameters);

            return hash;
        }
    }
}

/// <summary>
/// What the wrapper declares to satisfy the interface.
/// </summary>
/// <remarks>
/// Separate from the pipeline units because they do not line up: a property is one declaration and
/// up to two of them, each with its own state class and its own caller.
/// </remarks>
public enum DeclarationKind {
    Method,
    Property,
    Indexer,
    Event
}

/// <summary>
/// One declaration on the wrapper, and the pipeline units its accessors run through.
/// </summary>
/// <param name="Kind">What is being declared.</param>
/// <param name="Identifier">
/// The name as written: the method name, the property name, <c>this</c> for an indexer, or the event
/// name.
/// </param>
/// <param name="Type">
/// The property type or the event's handler type. Null for a method, whose return type lives on its
/// member instead.
/// </param>
/// <param name="Indices">The indexer's indices, empty for everything else.</param>
/// <param name="First">
/// Position in <see cref="InterceptorModel.Members"/> of the method, the getter, or the adder.
/// </param>
/// <param name="Second">
/// Position of the setter or the remover, or -1 when there is not one.
/// </param>
public record InterceptedDeclarationModel(
    DeclarationKind Kind,
    string Identifier,
    ITypeDefinition? Type,
    IReadOnlyList<InterceptedParameterModel> Indices,
    int First,
    int Second) {

    public virtual bool Equals(InterceptedDeclarationModel? other) {
        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other is null) {
            return false;
        }

        return Kind == other.Kind &&
               Identifier == other.Identifier &&
               Equals(Type, other.Type) &&
               First == other.First &&
               Second == other.Second &&
               ModelEquality.ListEquals(Indices, other.Indices);
    }

    public override int GetHashCode() {
        unchecked {
            var hash = (int)Kind;

            hash = hash * 31 + Identifier.GetHashCode();
            hash = hash * 31 + (Type?.GetHashCode() ?? 0);
            hash = hash * 31 + First;
            hash = hash * 31 + Second;
            hash = hash * 31 + ModelEquality.ListHashCode(Indices);

            return hash;
        }
    }
}

/// <summary>
/// A service to wrap, the interceptors to apply, and the members the wrapper must implement.
/// </summary>
/// <param name="Members">
/// The pipeline units, one state class each. Positional: a state class and its caller are named
/// after the position, so the order is part of the model.
/// </param>
/// <param name="Declarations">What the wrapper declares, pointing back into the members.</param>
/// <param name="TypeParameters">
/// The implementation's type parameters, empty for a non-generic service. The wrapper repeats them
/// and their constraints, so <c>Repository&lt;T&gt; where T : class</c> becomes
/// <c>Repository_Intercepted&lt;T&gt; : IRepository&lt;T&gt; where T : class</c> holding a
/// <c>Repository&lt;T&gt;</c>. A constraint that were dropped would leave the wrapper unable to
/// reference what it wraps.
/// </param>
public record InterceptorModel(
    ITypeDefinition ServiceType,
    ITypeDefinition ImplementationType,
    IReadOnlyList<InterceptorTypeModel> Interceptors,
    IReadOnlyList<InterceptedMemberModel> Members,
    IReadOnlyList<InterceptedDeclarationModel> Declarations,
    int Order,
    InterceptionRefusal? Refusal = null,
    IReadOnlyList<TypeParameterModel>? TypeParameters = null,
    ITypeDefinition? Realm = null,

    /// <summary>
    /// Where the intercepted class was declared, so DM0008 and DM0015 can point at it rather than
    /// at the project.
    /// </summary>
    LocationModel? Location = null) {

    /// <summary>
    /// Whether the intercepted service is an open generic, and so registers as an implementation type
    /// rather than through a factory.
    /// </summary>
    public bool IsOpenGeneric => TypeParameters is { Count: > 0 };

    /// <summary>
    /// Sentinel for a node carrying the attribute that produced no usable model and nothing to say
    /// about it.
    /// </summary>
    public static readonly InterceptorModel Ignore = new(
        TypeDefinition.Get("", "Ignore"),
        TypeDefinition.Get("", "Ignore"),
        Array.Empty<InterceptorTypeModel>(),
        Array.Empty<InterceptedMemberModel>(),
        Array.Empty<InterceptedDeclarationModel>(),
        0);

    /// <summary>
    /// A model that generates nothing and explains why, so an unsupported shape produces a
    /// diagnostic rather than a wrapper that does not compile.
    /// </summary>
    public static InterceptorModel Refused(string message, LocationModel? location = null) =>
        Ignore with { Refusal = new InterceptionRefusal(message), Location = location };

    public bool IsIgnored => ReferenceEquals(this, Ignore);
}

/// <summary>
/// Equality for the incremental pipeline. Every field affects the generated wrapper.
/// </summary>
public class InterceptorModelComparer : IEqualityComparer<InterceptorModel> {

    public bool Equals(InterceptorModel? x, InterceptorModel? y) {
        if (ReferenceEquals(x, y)) {
            return true;
        }

        if (x is null || y is null) {
            return false;
        }

        return x.Order == y.Order &&
               x.ServiceType.Equals(y.ServiceType) &&
               x.ImplementationType.Equals(y.ImplementationType) &&
               // Realm decides which module emits the applicator, so leaving it out meant editing
               // only `Realm = typeof(X)` compared equal to the model before the edit, hit the
               // cache and re-emitted nothing. DecoratorModelComparer and ServiceModelComparer both
               // compare theirs; this was the odd one out.
               Equals(x.Realm, y.Realm) &&
               Equals(x.Refusal, y.Refusal) &&
               ModelEquality.ListEquals(x.Interceptors, y.Interceptors) &&
               ModelEquality.ListEquals(x.Members, y.Members) &&
               ModelEquality.ListEquals(x.Declarations, y.Declarations) &&
               ModelEquality.ListEquals(x.TypeParameters, y.TypeParameters);
    }

    public int GetHashCode(InterceptorModel obj) {
        unchecked {
            var hash = obj.ServiceType.GetHashCode();

            hash = hash * 31 + obj.ImplementationType.GetHashCode();
            hash = hash * 31 + obj.Order;
            hash = hash * 31 + (obj.Realm?.GetHashCode() ?? 0);
            hash = hash * 31 + (obj.Refusal?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Interceptors);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Members);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Declarations);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.TypeParameters);

            return hash;
        }
    }
}

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
    ValueTaskOfValue
}

/// <summary>
/// A member of an intercepted interface, rendered to strings during the syntax transform.
/// </summary>
/// <remarks>
/// Strings rather than symbols: symbols are not equatable and holding one would defeat the
/// incremental cache, re-running generation on every keystroke.
/// </remarks>
/// <param name="Declaration">The member signature as it should be declared on the wrapper.</param>
/// <param name="Name">The member name, for the invocation context.</param>
/// <param name="ArgumentList">The arguments to forward, including any ref or out modifiers.</param>
/// <param name="ReturnShape">How the member returns.</param>
public record InterceptedMemberModel(
    string Declaration,
    string Name,
    string ArgumentList,
    ReturnShape ReturnShape);

/// <summary>
/// A service to wrap, the interceptors to apply, and the members the wrapper must implement.
/// </summary>
public record InterceptorModel(
    ITypeDefinition ServiceType,
    ITypeDefinition ImplementationType,
    IReadOnlyList<ITypeDefinition> Interceptors,
    IReadOnlyList<InterceptedMemberModel> Members,
    int Order) {

    /// <summary>
    /// Sentinel for a node carrying the attribute that produced no usable model.
    /// </summary>
    public static readonly InterceptorModel Ignore = new(
        TypeDefinition.Get("", "Ignore"),
        TypeDefinition.Get("", "Ignore"),
        Array.Empty<ITypeDefinition>(),
        Array.Empty<InterceptedMemberModel>(),
        0);

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
               ModelEquality.ListEquals(x.Interceptors, y.Interceptors) &&
               ModelEquality.ListEquals(x.Members, y.Members);
    }

    public int GetHashCode(InterceptorModel obj) {
        unchecked {
            var hash = obj.ServiceType.GetHashCode();
            hash = hash * 31 + obj.ImplementationType.GetHashCode();
            hash = hash * 31 + obj.Order;
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Interceptors);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Members);

            return hash;
        }
    }
}

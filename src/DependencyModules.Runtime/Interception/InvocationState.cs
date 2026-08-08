namespace DependencyModules.Runtime.Interception;

/// <summary>
/// The state a generated wrapper builds for one intercepted call.
/// </summary>
/// <remarks>
/// One instance per call. It holds the arguments as typed fields and dispatches each stage of the
/// pipeline, so nothing about a call is captured in a closure. Generated code derives from one of the
/// three typed subclasses below; nothing else should, and an interceptor never names this type.
///
/// This is what lets the pipeline run without delegates. An
/// <see cref="InvocationContext{TResult}"/> is a struct pointing at one of these plus a stage index,
/// so proceeding is a virtual call rather than a closure allocation and a <c>Func</c> per interceptor.
/// </remarks>
public abstract class InvocationState : IArguments {
    /// <summary>
    /// The member being invoked.
    /// </summary>
    public abstract CallerInfo Caller { get; }

    /// <inheritdoc />
    public abstract int Count { get; }

    /// <inheritdoc />
    public abstract object? this[int index] { get; set; }

    /// <inheritdoc />
    public abstract string NameAt(int index);
}

/// <summary>
/// Invocation state for a member that returns its result directly.
/// </summary>
/// <typeparam name="TResult">
/// The member's return type, or <see cref="NoResult"/> when it returns void.
/// </typeparam>
public abstract class InvocationState<TResult> : InvocationState {
    /// <summary>
    /// Runs the pipeline from <paramref name="stage"/> onwards.
    /// </summary>
    /// <param name="stage">
    /// The position to run: the interceptor registered at that position, or the intercepted
    /// implementation once every interceptor has been through.
    /// </param>
    public abstract TResult Invoke(int stage);
}

/// <summary>
/// Invocation state for a member returning a task.
/// </summary>
/// <typeparam name="TResult">
/// The type the task produces, or <see cref="NoResult"/> for a task with no result.
/// </typeparam>
public abstract class AsyncInvocationState<TResult> : InvocationState {
    /// <summary>
    /// Runs the pipeline from <paramref name="stage"/> onwards.
    /// </summary>
    /// <param name="stage">
    /// The position to run: the interceptor registered at that position, or the intercepted
    /// implementation once every interceptor has been through.
    /// </param>
    public abstract ValueTask<TResult> Invoke(int stage);
}

/// <summary>
/// Invocation state for a member returning an async stream.
/// </summary>
/// <typeparam name="TItem">The type the stream yields.</typeparam>
public abstract class StreamInvocationState<TItem> : InvocationState {
    /// <summary>
    /// Runs the pipeline from <paramref name="stage"/> onwards.
    /// </summary>
    /// <param name="stage">
    /// The position to run: the interceptor registered at that position, or the intercepted
    /// implementation once every interceptor has been through.
    /// </param>
    public abstract IAsyncEnumerable<TItem> Invoke(int stage);
}

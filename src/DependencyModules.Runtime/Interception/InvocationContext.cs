namespace DependencyModules.Runtime.Interception;

/// <summary>
/// The call an interceptor is running around, for a member that returns its result directly.
/// </summary>
/// <remarks>
/// This is the one object an interceptor is handed. It is a struct holding the call's state and this
/// interceptor's position in the pipeline. The position is carried here rather than mutated on the
/// state, so <see cref="Proceed"/> can be called more than once — a retry re-enters the same next
/// stage instead of walking further down the pipeline — and calling it after an await still resumes
/// from the right place.
/// </remarks>
/// <typeparam name="TResult">
/// The member's return type, or <see cref="NoResult"/> when it returns void.
/// </typeparam>
public readonly struct InvocationContext<TResult> {
    private readonly InvocationState<TResult> _state;
    private readonly int _stage;

    /// <summary>
    /// Constructed by generated code.
    /// </summary>
    /// <param name="state">The state for this call.</param>
    /// <param name="stage">The position of the interceptor receiving this context.</param>
    public InvocationContext(InvocationState<TResult> state, int stage) {
        _state = state;
        _stage = stage;
    }

    /// <summary>
    /// The member being invoked.
    /// </summary>
    public CallerInfo Caller => _state.Caller;

    /// <summary>
    /// The arguments, which may be replaced before proceeding.
    /// </summary>
    public IArguments Arguments => _state;

    /// <summary>
    /// Runs the rest of the pipeline and returns its result. Not calling it skips the implementation
    /// and every interceptor below this one.
    /// </summary>
    public TResult Proceed() => _state.Invoke(_stage + 1);
}

/// <summary>
/// The call an interceptor is running around, for a member returning a task.
/// </summary>
/// <remarks>
/// <see cref="ProceedAsync"/> is awaited inside the interceptor, so work done after awaiting it
/// happens when the call has finished rather than when its task was handed back.
/// </remarks>
/// <typeparam name="TResult">
/// The type the task produces, or <see cref="NoResult"/> for a task with no result.
/// </typeparam>
public readonly struct AsyncInvocationContext<TResult> {
    private readonly AsyncInvocationState<TResult> _state;
    private readonly int _stage;

    /// <summary>
    /// Constructed by generated code.
    /// </summary>
    /// <param name="state">The state for this call.</param>
    /// <param name="stage">The position of the interceptor receiving this context.</param>
    public AsyncInvocationContext(AsyncInvocationState<TResult> state, int stage) {
        _state = state;
        _stage = stage;
    }

    /// <summary>
    /// The member being invoked.
    /// </summary>
    public CallerInfo Caller => _state.Caller;

    /// <summary>
    /// The arguments, which may be replaced before proceeding.
    /// </summary>
    public IArguments Arguments => _state;

    /// <summary>
    /// Runs the rest of the pipeline. Not calling it skips the implementation and every interceptor
    /// below this one.
    /// </summary>
    public ValueTask<TResult> ProceedAsync() => _state.Invoke(_stage + 1);
}

/// <summary>
/// The call an interceptor is running around, for a member returning an async stream.
/// </summary>
/// <remarks>
/// The stream returned by <see cref="Proceed"/> is enumerated by the interceptor, so an interceptor
/// observes each item as it is produced rather than only the call that produced the stream.
/// </remarks>
/// <typeparam name="TItem">The type the stream yields.</typeparam>
public readonly struct StreamInvocationContext<TItem> {
    private readonly StreamInvocationState<TItem> _state;
    private readonly int _stage;

    /// <summary>
    /// Constructed by generated code.
    /// </summary>
    /// <param name="state">The state for this call.</param>
    /// <param name="stage">The position of the interceptor receiving this context.</param>
    public StreamInvocationContext(StreamInvocationState<TItem> state, int stage) {
        _state = state;
        _stage = stage;
    }

    /// <summary>
    /// The member being invoked.
    /// </summary>
    public CallerInfo Caller => _state.Caller;

    /// <summary>
    /// The arguments, which may be replaced before proceeding.
    /// </summary>
    public IArguments Arguments => _state;

    /// <summary>
    /// Runs the rest of the pipeline and returns the stream it produces. Not calling it skips the
    /// implementation and every interceptor below this one.
    /// </summary>
    public IAsyncEnumerable<TItem> Proceed() => _state.Invoke(_stage + 1);
}

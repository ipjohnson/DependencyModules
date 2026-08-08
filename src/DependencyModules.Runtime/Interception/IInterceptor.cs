namespace DependencyModules.Runtime.Interception;

/// <summary>
/// Runs around calls to an intercepted service that return their result directly.
/// </summary>
/// <remarks>
/// One interceptor applies to any number of unrelated services. That cannot be written by hand,
/// because C# does not allow a type parameter as a base type, so the wrapper implementing each
/// service is generated. The interface is not generic and the method is, so the return type is
/// supplied by the generated call site: nothing is boxed and nothing is inspected at run time.
///
/// A member returning a task cannot be wrapped by this interface, because the surrounding code would
/// have nowhere to await and would report completion when the task was handed back rather than when
/// the work finished. Implement <see cref="IAsyncInterceptor"/> for those, and
/// <see cref="IAsyncEnumerableInterceptor"/> for members returning an async stream. A type may
/// implement any combination; the generator picks per member and reports the members an interceptor
/// cannot serve rather than skipping them silently.
///
/// <code>
/// public TResult Intercept&lt;TResult&gt;(InvocationContext&lt;TResult&gt; context) {
///     var stopwatch = Stopwatch.StartNew();
///
///     try {
///         return context.Proceed();
///     } finally {
///         _log.Record(context.Caller, stopwatch.Elapsed);
///     }
/// }
/// </code>
/// </remarks>
public interface IInterceptor {
    /// <summary>
    /// Wraps one call. Call <see cref="InvocationContext{TResult}.Proceed"/> to run the rest of the
    /// pipeline, more than once to retry, or not at all to return without reaching the
    /// implementation.
    /// </summary>
    /// <param name="context">The call being intercepted.</param>
    /// <typeparam name="TResult">
    /// The member's return type, or <see cref="NoResult"/> when it returns void.
    /// </typeparam>
    TResult Intercept<TResult>(InvocationContext<TResult> context);
}

/// <summary>
/// Runs around calls to an intercepted service that return a task.
/// </summary>
/// <remarks>
/// The generated wrapper awaits nothing on the interceptor's behalf: an interceptor awaits
/// <see cref="AsyncInvocationContext{TResult}.ProceedAsync"/> itself, so anything it does afterwards
/// happens once the work has finished. Because the interceptor holds the call in a single method
/// body, state spanning the call is an ordinary local and a scope may be held across it.
///
/// <code>
/// public async ValueTask&lt;TResult&gt; InterceptAsync&lt;TResult&gt;(AsyncInvocationContext&lt;TResult&gt; context) {
///     using var scope = _tracer.StartSpan(context.Caller.MemberName);
///
///     return await context.ProceedAsync();
/// }
/// </code>
/// </remarks>
public interface IAsyncInterceptor {
    /// <summary>
    /// Wraps one call. Call <see cref="AsyncInvocationContext{TResult}.ProceedAsync"/> to run the
    /// rest of the pipeline, more than once to retry, or not at all to return without reaching the
    /// implementation.
    /// </summary>
    /// <param name="context">The call being intercepted.</param>
    /// <typeparam name="TResult">
    /// The type the task produces, or <see cref="NoResult"/> for a task with no result.
    /// </typeparam>
    ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context);
}

/// <summary>
/// Runs around calls to an intercepted service that return an async stream.
/// </summary>
/// <remarks>
/// A member returning <see cref="IAsyncEnumerable{T}"/> hands its stream back immediately, so
/// wrapping it as an ordinary value would measure the construction of the iterator and nothing else.
/// An interceptor here enumerates the stream, and so observes each item as it is produced.
///
/// <code>
/// public async IAsyncEnumerable&lt;TItem&gt; InterceptStream&lt;TItem&gt;(StreamInvocationContext&lt;TItem&gt; context) {
///     var count = 0;
///
///     await foreach (var item in context.Proceed()) {
///         count++;
///         yield return item;
///     }
///
///     _log.Record(context.Caller, count);
/// }
/// </code>
/// </remarks>
public interface IAsyncEnumerableInterceptor {
    /// <summary>
    /// Wraps one call. Enumerate <see cref="StreamInvocationContext{TItem}.Proceed"/> to yield the
    /// implementation's items, or yield something else to replace them.
    /// </summary>
    /// <param name="context">The call being intercepted.</param>
    /// <typeparam name="TItem">The type the stream yields.</typeparam>
    IAsyncEnumerable<TItem> InterceptStream<TItem>(StreamInvocationContext<TItem> context);
}

namespace DependencyModules.Runtime.Interception;

/// <summary>
/// Runs around every call to an intercepted service.
/// </summary>
/// <remarks>
/// One interceptor applies to any number of unrelated services. That cannot be written by hand,
/// because C# does not allow a type parameter as a base type, so the wrapper implementing each
/// service is generated.
///
/// For a method returning a task, the generated wrapper awaits the inner call before reporting
/// completion, so <see cref="OnExit"/> reflects when the work finished rather than when the task was
/// handed back.
/// </remarks>
public interface IInterceptor {
    /// <summary>
    /// Called before the intercepted member runs.
    /// </summary>
    void OnEnter(in InvocationContext context) { }

    /// <summary>
    /// Called after the intercepted member completes successfully.
    /// </summary>
    void OnExit(in InvocationContext context) { }

    /// <summary>
    /// Called when the intercepted member throws. The exception is rethrown afterwards.
    /// </summary>
    void OnError(in InvocationContext context, Exception exception) { }
}

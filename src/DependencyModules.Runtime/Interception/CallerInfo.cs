namespace DependencyModules.Runtime.Interception;

/// <summary>
/// Identifies the member an interceptor is running around.
/// </summary>
/// <remarks>
/// Everything here is known when the wrapper is generated, so one of these is emitted per member as a
/// static readonly value and the same instance is handed to every call. Per-call data — the
/// arguments — lives on the invocation context instead, where it can be read and replaced.
///
/// Property, indexer and event accessors are named the way the CLR names them: <c>get_Count</c>,
/// <c>set_Count</c>, <c>add_Changed</c>. A getter and a setter are therefore distinguishable, and the
/// name matches what appears in a stack trace.
/// </remarks>
/// <param name="serviceType">The interface being intercepted.</param>
/// <param name="memberName">The member being invoked.</param>
public readonly struct CallerInfo(Type serviceType, string memberName) {
    /// <summary>
    /// The intercepted interface.
    /// </summary>
    public Type ServiceType { get; } = serviceType;

    /// <summary>
    /// The member being invoked.
    /// </summary>
    public string MemberName { get; } = memberName;

    /// <summary>
    /// The intercepted member, as Interface.Member.
    /// </summary>
    public override string ToString() => $"{ServiceType.Name}.{MemberName}";
}

namespace DependencyModules.Runtime.Interception;

/// <summary>
/// Identifies the call an interceptor is running around.
/// </summary>
/// <remarks>
/// A readonly struct passed by <c>in</c>, carrying only metadata. Arguments are deliberately absent:
/// capturing them would mean boxing every value type and allocating an array on every call, which is
/// the cost that makes runtime proxies unsuitable for hot paths. The generated wrapper knows the
/// signature at compile time and calls straight through.
/// </remarks>
/// <param name="serviceType">The interface being intercepted.</param>
/// <param name="memberName">The member being invoked.</param>
public readonly struct InvocationContext(Type serviceType, string memberName) {
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

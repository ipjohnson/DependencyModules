namespace DependencyModules.Runtime.Interception;

/// <summary>
/// Stands in for the result of a member that does not produce one.
/// </summary>
/// <remarks>
/// A void member, a <see cref="System.Threading.Tasks.Task"/> and a
/// <see cref="System.Threading.Tasks.ValueTask"/> all flow through the same single interceptor method
/// as everything else, rather than each interceptor having to implement an overload it does not care
/// about. An interceptor written generically never names this type.
/// </remarks>
public readonly struct NoResult {
    /// <summary>
    /// Renders as <c>void</c>, so an interceptor logging a result does not print a type name.
    /// </summary>
    public override string ToString() => "void";
}

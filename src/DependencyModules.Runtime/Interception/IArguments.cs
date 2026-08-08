namespace DependencyModules.Runtime.Interception;

/// <summary>
/// The arguments of the call an interceptor is running around, readable and replaceable by position.
/// </summary>
/// <remarks>
/// The values live on the invocation state as typed fields, which is where the inner call reads them
/// from. An argument therefore costs nothing until an interceptor looks at it: reading one boxes it,
/// writing one replaces the value the implementation will receive, and an interceptor that ignores
/// them pays for neither.
/// </remarks>
public interface IArguments {
    /// <summary>
    /// The number of arguments the intercepted member declares.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// The argument at <paramref name="index"/>. Assigning replaces the value passed on to the
    /// implementation.
    /// </summary>
    /// <param name="index">Position of the argument, in declaration order.</param>
    object? this[int index] { get; set; }

    /// <summary>
    /// The declared name of the argument at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">Position of the argument, in declaration order.</param>
    string NameAt(int index);
}

using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// The plain <see cref="ITestMethodContext"/>, for a runner that has no test-method model of its own.
/// </summary>
/// <param name="method">The test method.</param>
/// <param name="attributes">
/// The attribute instances of the test, widest scope first: the assembly, then the class, then the
/// method.
/// </param>
public sealed class TestMethodContext(MethodInfo method, IReadOnlyList<Attribute> attributes)
    : ITestMethodContext
{
    /// <inheritdoc />
    public MethodInfo Method { get; } = method ?? throw new ArgumentNullException(nameof(method));

    /// <inheritdoc />
    public IReadOnlyList<Attribute> Attributes { get; } =
        attributes ?? throw new ArgumentNullException(nameof(attributes));
}

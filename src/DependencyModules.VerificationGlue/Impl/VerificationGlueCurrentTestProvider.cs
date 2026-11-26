using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// The <see cref="ICurrentTestProvider"/> registered in each test container.
/// </summary>
/// <remarks>
/// VerificationGlue.Tests has no ambient current test, so this answers for the test its container
/// was built for, and the static <c>CurrentTest</c> stays unset. A service that takes an
/// <see cref="ICurrentTestProvider"/>, such as <c>TestOutputLoggerProvider</c>, writes to the
/// test's output.
/// </remarks>
internal sealed class VerificationGlueCurrentTestProvider(
    TestContainerContext context,
    VerificationGlueTestMethodContext methodContext
) : ICurrentTestProvider
{
    public object? Key => context.Scope;

    public string? DisplayName => context.Uid;

    public Assembly? Assembly => methodContext.MethodOrNull?.DeclaringType?.Assembly;

    public bool TryWriteLine(string message)
    {
        context.Output.WriteLine(message);

        return true;
    }
}

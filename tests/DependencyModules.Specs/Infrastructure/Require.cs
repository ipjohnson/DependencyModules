using System.Diagnostics.CodeAnalysis;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// The harness checks. They throw a plain exception, which fails the test on any runner, because
/// the harness is compiled into an xUnit project and a VerificationGlue.Tests project.
/// </summary>
public static class Require
{
    public static void That([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    [DoesNotReturn]
    public static void Fail(string message) => throw new HarnessCheckException(message);
}

public sealed class HarnessCheckException(string message) : Exception(message);

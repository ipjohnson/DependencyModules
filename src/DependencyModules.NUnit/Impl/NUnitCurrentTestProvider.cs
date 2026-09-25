using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace DependencyModules.NUnit.Impl;

/// <summary>
/// Reads the running test from NUnit's <see cref="TestExecutionContext.CurrentContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Installed from the static constructor of <c>ModuleTestAttribute</c>. NUnit creates the attribute
/// while it builds the tests, before the first one runs.
/// </para>
/// <para>
/// The key is NUnit's <see cref="Test"/>, which the iterations of a <c>[Repeat]</c> or a
/// <c>[Retry]</c> share: NUnit makes no object of its own for one iteration. The container is still
/// built for each iteration.
/// </para>
/// </remarks>
internal sealed class NUnitCurrentTestProvider : ICurrentTestProvider
{
    public object? Key => RunningTest;

    public string? DisplayName => RunningTest?.FullName;

    public Assembly? Assembly => RunningTest?.TypeInfo?.Assembly;

    public bool TryWriteLine(string message)
    {
        if (RunningTest == null)
        {
            return false;
        }

        TestContext.Out.WriteLine(message);

        return true;
    }

    public static void Install() => CurrentTest.Provider ??= new NUnitCurrentTestProvider();

    // NUnit answers outside a test with its ad hoc context rather than with null.
    private static Test? RunningTest =>
        TestExecutionContext.CurrentContext is TestExecutionContext.AdhocContext
            ? null
            : TestExecutionContext.CurrentContext.CurrentTest;
}

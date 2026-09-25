using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Xunit;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Reads the running test from xUnit's <see cref="TestContext.Current"/>.
/// </summary>
/// <remarks>
/// <para>
/// Installed from the static constructors of <see cref="ModuleTestCase"/> and of
/// <c>ModuleTestAttribute</c>. xUnit creates both while it discovers or deserializes the tests,
/// before the first one runs.
/// </para>
/// <para>
/// xUnit has no running test while a case builds its containers: the case creates its tests, and
/// the container for each, before xUnit runs the first of them. So <see cref="Key"/> and
/// <see cref="DisplayName"/> are null in the hooks that build a container, and a line written there
/// is dropped. <see cref="Assembly"/> answers there, from the test class.
/// </para>
/// </remarks>
internal sealed class XunitCurrentTestProvider : ICurrentTestProvider
{
    public object? Key => TestContext.Current.Test;

    public string? DisplayName => TestContext.Current.Test?.TestDisplayName;

    public Assembly? Assembly =>
        TestContext.Current.TestClass is IXunitTestClass testClass
            ? testClass.Class.Assembly
            : null;

    public bool TryWriteLine(string message)
    {
        if (TestContext.Current.TestOutputHelper is not { } output)
        {
            return false;
        }

        try
        {
            output.WriteLine(message);

            return true;
        }
        // The flow still carries the context of a test that has finished, and xUnit's helper
        // refuses output that it can no longer report.
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void Install() => CurrentTest.Provider ??= new XunitCurrentTestProvider();
}

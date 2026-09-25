using System.Reflection;

namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// Reads the running test from the test framework's own context.
/// </summary>
/// <remarks>
/// <para>
/// The ambient counterpart of <see cref="ITestMethodContext"/>. That one is handed to the hooks that
/// build a test's container. This one answers from anywhere the framework's context flows, such as
/// an assertion helper or a logger, with nothing handed to it. Each runner package installs its own
/// in <c>CurrentTest.Provider</c>: DependencyModules.xUnit and DependencyModules.xUnit4 read
/// <c>TestContext.Current</c>, and DependencyModules.NUnit reads
/// <c>TestExecutionContext.CurrentContext</c>.
/// </para>
/// <para>
/// Every member answers for the test running on the calling flow, and with null or false when there
/// is none. A library that reads this works under any runner package and references no framework.
/// </para>
/// </remarks>
public interface ICurrentTestProvider
{
    /// <summary>
    /// The framework's own object for the running test, or null when no test is running.
    /// </summary>
    /// <remarks>
    /// Compared by reference. Two tests that run at the same time have different keys, and the
    /// framework holds the object for as long as the test runs, so a weak table can hold per-test
    /// state against it.
    /// </remarks>
    object? Key { get; }

    /// <summary>
    /// The framework's display name for the running test, or null when no test is running.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// The assembly that declares the running test's class, or null when there is no test class.
    /// </summary>
    Assembly? Assembly { get; }

    /// <summary>
    /// Writes a line to the running test's output.
    /// </summary>
    /// <returns>
    /// False, having written nothing, when no test is running or the test has finished.
    /// </returns>
    bool TryWriteLine(string message);
}

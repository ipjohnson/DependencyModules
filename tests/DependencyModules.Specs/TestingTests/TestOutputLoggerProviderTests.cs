using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.Logging;

namespace DependencyModules.Specs.TestingTests;

/// <summary>
/// What the logger writes, read from a provider of the test's own rather than from a framework's
/// output.
/// </summary>
/// <remarks>
/// Through the constructor that takes a provider, because <c>CurrentTest.Provider</c> is one static
/// for the whole assembly, and this assembly runs its tests in parallel. The path through
/// <c>CurrentTest</c> is asserted under each framework, in SutProject.Tests and
/// SutProject.NUnitTests.
/// </remarks>
public partial class TestOutputLoggerProviderTests
{
    private sealed class RecordingTest(bool running = true) : ICurrentTestProvider
    {
        public List<string> Lines { get; } = [];

        public object? Key => running ? this : null;

        public string? DisplayName => running ? nameof(RecordingTest) : null;

        public Assembly? Assembly => null;

        public bool TryWriteLine(string message)
        {
            if (!running)
            {
                return false;
            }

            Lines.Add(message);

            return true;
        }
    }

    [VerifyTest]
    public void AnEntryIsOneLineInTheConsoleLoggersSingleLineFormat()
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test)
            .CreateLogger("Shop.OrderService")
            .LogInformation(new EventId(7), "Order {Id} accepted", 42);

        Verify(test.Lines.SequenceEqual(["info: Shop.OrderService[7] Order 42 accepted"]));
    }

    [VerifyTest]
    [Row(LogLevel.Trace, "trce")]
    [Row(LogLevel.Debug, "dbug")]
    [Row(LogLevel.Information, "info")]
    [Row(LogLevel.Warning, "warn")]
    [Row(LogLevel.Error, "fail")]
    [Row(LogLevel.Critical, "crit")]
    public void EachLevelHasTheConsoleLoggersName(LogLevel level, string name)
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test).CreateLogger("Category").Log(level, "message");

        Verify(test.Lines.SequenceEqual([$"{name}: Category[0] message"]));
    }

    [VerifyTest]
    public void AnExceptionIsOnTheLinesAfterTheEntry()
    {
        var test = new RecordingTest();
        var exception = new InvalidOperationException("The order was rejected.");

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogError(exception, "failed");

        Verify(
            test.Lines.SequenceEqual(["fail: Category[0] failed" + Environment.NewLine + exception])
        );
    }

    [VerifyTest]
    public void NoneIsNotEnabled()
    {
        var test = new RecordingTest();
        var logger = new TestOutputLoggerProvider(test).CreateLogger("Category");

        logger.Log(LogLevel.None, "message");

        Verify(!logger.IsEnabled(LogLevel.None));
        Verify(test.Lines.Count == 0);
    }

    [VerifyTest]
    public void AnEntryWithNothingToShowIsDropped()
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogInformation("");

        Verify(test.Lines.Count == 0);
    }

    /// <summary>
    /// The case a logger meets on a background thread after its test has finished. A logger that
    /// threw here would fail the application under test instead of the test.
    /// </summary>
    [VerifyTest]
    public void AnEntryLoggedWhileNoTestIsRunningIsDropped()
    {
        var test = new RecordingTest(running: false);

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogInformation("message");

        Verify(test.Lines.Count == 0);
    }

    [VerifyTest]
    public void AScopeChangesNothing()
    {
        var test = new RecordingTest();
        var logger = new TestOutputLoggerProvider(test).CreateLogger("Category");

        using (logger.BeginScope("scope"))
        {
            logger.LogInformation("message");
        }

        Verify(test.Lines.SequenceEqual(["info: Category[0] message"]));
    }
}

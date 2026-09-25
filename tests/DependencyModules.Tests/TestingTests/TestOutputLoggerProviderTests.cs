using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DependencyModules.Tests.TestingTests;

/// <summary>
/// What the logger writes, read from a provider of the test's own rather than from a framework's
/// output.
/// </summary>
/// <remarks>
/// Through the constructor that takes a provider, because <c>CurrentTest.Provider</c> is one static
/// for the whole assembly, and this assembly runs its test classes in parallel. The path through
/// <c>CurrentTest</c> is asserted under each framework, in SutProject.Tests and
/// SutProject.NUnitTests.
/// </remarks>
public class TestOutputLoggerProviderTests
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

    [Fact]
    public void AnEntryIsOneLineInTheConsoleLoggersSingleLineFormat()
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test)
            .CreateLogger("Shop.OrderService")
            .LogInformation(new EventId(7), "Order {Id} accepted", 42);

        Assert.Equal(["info: Shop.OrderService[7] Order 42 accepted"], test.Lines);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trce")]
    [InlineData(LogLevel.Debug, "dbug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "fail")]
    [InlineData(LogLevel.Critical, "crit")]
    public void EachLevelHasTheConsoleLoggersName(LogLevel level, string name)
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test).CreateLogger("Category").Log(level, "message");

        Assert.Equal([$"{name}: Category[0] message"], test.Lines);
    }

    [Fact]
    public void AnExceptionIsOnTheLinesAfterTheEntry()
    {
        var test = new RecordingTest();
        var exception = new InvalidOperationException("The order was rejected.");

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogError(exception, "failed");

        Assert.Equal(["fail: Category[0] failed" + Environment.NewLine + exception], test.Lines);
    }

    [Fact]
    public void NoneIsNotEnabled()
    {
        var test = new RecordingTest();
        var logger = new TestOutputLoggerProvider(test).CreateLogger("Category");

        logger.Log(LogLevel.None, "message");

        Assert.False(logger.IsEnabled(LogLevel.None));
        Assert.Empty(test.Lines);
    }

    [Fact]
    public void AnEntryWithNothingToShowIsDropped()
    {
        var test = new RecordingTest();

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogInformation("");

        Assert.Empty(test.Lines);
    }

    /// <summary>
    /// The case a logger meets on a background thread after its test has finished. A logger that
    /// threw here would fail the application under test instead of the test.
    /// </summary>
    [Fact]
    public void AnEntryLoggedWhileNoTestIsRunningIsDropped()
    {
        var test = new RecordingTest(running: false);

        new TestOutputLoggerProvider(test).CreateLogger("Category").LogInformation("message");

        Assert.Empty(test.Lines);
    }

    [Fact]
    public void AScopeChangesNothing()
    {
        var test = new RecordingTest();
        var logger = new TestOutputLoggerProvider(test).CreateLogger("Category");

        using (logger.BeginScope("scope"))
        {
            logger.LogInformation("message");
        }

        Assert.Equal(["info: Category[0] message"], test.Lines);
    }
}

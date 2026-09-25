using System.Collections.Concurrent;
using System.Text;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.Logging;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// A logger provider that writes each entry to the output of the running test.
/// </summary>
/// <remarks>
/// <para>
/// An entry is one line in the console logger's single-line format,
/// <c>info: Shop.OrderService[0] Order 42 accepted</c>, with an exception on the lines after it.
/// An entry logged while no test is running is dropped, because there is no output to write it to.
/// </para>
/// <para>
/// Registered in a test's container as an <see cref="ILoggerProvider"/>, it puts the log of the
/// application under test into the test's output. The container's logger factory writes to it
/// beside any other provider.
/// </para>
/// </remarks>
public sealed class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly ICurrentTestProvider? _currentTest;

    private readonly ConcurrentDictionary<string, TestOutputLogger> _loggers = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// Writes through <see cref="CurrentTest.Provider"/>, read again for each entry.
    /// </summary>
    public TestOutputLoggerProvider() { }

    /// <summary>
    /// Writes through <paramref name="currentTest"/>.
    /// </summary>
    public TestOutputLoggerProvider(ICurrentTestProvider currentTest)
    {
        ArgumentNullException.ThrowIfNull(currentTest);

        _currentTest = currentTest;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new TestOutputLogger(name, this));

    /// <inheritdoc />
    public void Dispose() { }

    private void Write(string entry) => (_currentTest ?? CurrentTest.Provider)?.TryWriteLine(entry);

    private sealed class TestOutputLogger(string categoryName, TestOutputLoggerProvider provider)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            // What the console logger does with an entry that has nothing to show.
            if (string.IsNullOrEmpty(message) && exception == null)
            {
                return;
            }

            var entry = new StringBuilder()
                .Append(LevelName(logLevel))
                .Append(": ")
                .Append(categoryName)
                .Append('[')
                .Append(eventId.Id)
                .Append(']');

            if (!string.IsNullOrEmpty(message))
            {
                entry.Append(' ').Append(message);
            }

            if (exception != null)
            {
                entry.AppendLine().Append(exception);
            }

            provider.Write(entry.ToString());
        }

        private static string LevelName(LogLevel logLevel) =>
            logLevel switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => logLevel.ToString(),
            };
    }
}

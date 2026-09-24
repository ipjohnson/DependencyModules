using System.Text;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class FileLogger : IDisposable
{
    private readonly string _loggerName;
    private readonly string _outputFolder;
    private StringBuilder? _sb;

    /// <summary>
    /// Runs <paramref name="logger"/>, recording any failure to the log and reporting it through
    /// <paramref name="reportFailure"/>.
    /// </summary>
    /// <remarks>
    /// The exception must not be swallowed. Catching it here also stops Roslyn from reporting its
    /// own CS8785, so without <paramref name="reportFailure"/> a crashing generator produced a
    /// successful build, no registrations, and no message of any kind.
    /// </remarks>
    public static void Wrap(
        string loggerName,
        DependencyModuleConfigurationModel configurationModel,
        Action<FileLogger> logger,
        Action<Exception>? reportFailure = null
    )
    {
        var fileLogger = new FileLogger(configurationModel, loggerName);
        try
        {
            logger(fileLogger);
        }
        // Cancellation is Roslyn stopping the run, not a failure of the generator.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            fileLogger.Error($"{e.Message}\n{e.StackTrace}");

            if (reportFailure == null)
            {
                throw;
            }

            reportFailure(e);
        }
        finally
        {
            fileLogger.Dispose();
        }
    }

    /// <summary>
    /// Runs one output of the generator, and reports a failure as DM0001.
    /// </summary>
    /// <remarks>
    /// Every output needs this, including the ones that only report diagnostics. Roslyn answers an
    /// exception that escapes any output with CS8785 and drops every file the generator wrote in
    /// that run, so each module then fails <c>AddModule</c> with CS0311.
    /// </remarks>
    public static void Wrap(
        string loggerName,
        DependencyModuleConfigurationModel configurationModel,
        SourceProductionContext context,
        Action<FileLogger> logger
    ) =>
        Wrap(
            loggerName,
            configurationModel,
            logger,
            exception =>
                context.ReportDiagnostic(
                    DependencyModuleDiagnostics.GeneratorFailureFrom(exception)
                )
        );

    public FileLogger(DependencyModuleConfigurationModel configurationModel, string loggerName)
    {
        _loggerName = loggerName;
        _outputFolder = configurationModel.LogOutputFolder;
        if (!string.IsNullOrEmpty(_outputFolder))
        {
            _sb = new StringBuilder();
        }
    }

    public void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public void Info(string message, object data)
    {
        WriteLog("INFO", message, data);
    }

    public void Error(string message)
    {
        WriteLog("ERROR", message);
    }

    public void Error(string message, object data)
    {
        WriteLog("ERROR", message, data);
    }

    private void WriteLog(string level, string message, object? data = null)
    {
        if (_sb != null)
        {
            _sb.AppendLine($"{level}: {message}");
            if (data != null)
            {
                _sb.AppendLine(data.ToString());
            }
        }
    }

    public void Dispose()
    {
        if (_sb == null)
        {
            return;
        }

        // The time alone is not unique. Two outputs of one stage often finish in the same
        // millisecond, and File.WriteAllText replaces a file that has the same name.
        var fileName =
            $"{_loggerName}.{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.txt";

#pragma warning disable RS1035
        try
        {
            // _sb is only allocated when an output folder was configured, so honour it here rather
            // than dropping the log into whatever directory the compiler happens to be running in.
            Directory.CreateDirectory(_outputFolder);
            File.WriteAllText(Path.Combine(_outputFolder, fileName), _sb.ToString());
        }
        catch (Exception)
        {
            // Diagnostic logging must never fail a build.
        }
#pragma warning restore RS1035
    }
}

using System.Text;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class FileLogger : IDisposable {
    private readonly string _loggerName;
    private readonly string _outputFolder;
    private StringBuilder? _sb;

    public static void Wrap(string loggerName, DependencyModuleConfigurationModel configurationModel, Action<FileLogger> logger) {
        var fileLogger = new FileLogger(configurationModel, loggerName);
        try {
            logger(fileLogger);
        }
        catch (Exception e) {
            fileLogger.Error($"{e.Message}\n{e.StackTrace}");
        }
        finally {
            fileLogger.Dispose();
        }
    }
    
    public FileLogger(DependencyModuleConfigurationModel configurationModel, string loggerName) {
        _loggerName = loggerName;
        _outputFolder = configurationModel.LogOutputFolder;
        if (!string.IsNullOrEmpty(_outputFolder)) {
            _sb = new StringBuilder();
        }
    }

    public void Info(string message) {
        WriteLog("INFO", message);
    }

    public void Info(string message, object data) {
        WriteLog("INFO", message, data);
    }
    
    public void Error(string message) {
        WriteLog("ERROR", message);
    }
    
    public void Error(string message, object data) {
        WriteLog("ERROR", message, data);
    }

    private void WriteLog(string level, string message, object? data = null) {
        if (_sb != null) {
            _sb.AppendLine($"{level}: {message}");
            if (data != null) {
                _sb.AppendLine(data.ToString());
            }
        }
    }
    
    public void Dispose() {
        if (_sb == null) {
            return;
        }

        var fileName = $"{_loggerName}.{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.txt";

#pragma warning disable RS1035
        try {
            // _sb is only allocated when an output folder was configured, so honour it here rather
            // than dropping the log into whatever directory the compiler happens to be running in.
            Directory.CreateDirectory(_outputFolder);
            File.WriteAllText(Path.Combine(_outputFolder, fileName), _sb.ToString());
        }
        catch (Exception) {
            // Diagnostic logging must never fail a build.
        }
#pragma warning restore RS1035
    }
}
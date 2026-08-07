using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// FileLogger backs the DependencyModules_LogOutputDirectory build property. It runs inside the
/// compiler, so it must be inert unless explicitly switched on and must never fail a build.
/// </summary>
public class FileLoggerTests : IDisposable {
    private readonly string _outputFolder =
        Path.Combine(Path.GetTempPath(), "DependencyModulesLoggerTests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void WithNoOutputFolder_WritesNothing() {
        var before = Directory.GetCurrentDirectory();
        var filesBefore = Directory.GetFiles(before);

        using (var logger = new FileLogger(Configuration(logOutputFolder: ""), "test")) {
            logger.Info("a message");
            logger.Error("a problem");
        }

        Assert.Equal(filesBefore.Length, Directory.GetFiles(before).Length);
    }

    /// <summary>
    /// Regression test: logs used to land in the compiler's working directory, ignoring the
    /// configured folder entirely.
    /// </summary>
    [Fact]
    public void WithAnOutputFolder_WritesIntoThatFolder() {
        using (var logger = new FileLogger(Configuration(_outputFolder), "generator")) {
            logger.Info("a message");
        }

        var written = Directory.GetFiles(_outputFolder);

        var file = Assert.Single(written);
        Assert.StartsWith("generator.", Path.GetFileName(file));
        Assert.EndsWith(".txt", file);
    }

    [Fact]
    public void WithAnOutputFolder_CreatesTheFolderIfMissing() {
        Assert.False(Directory.Exists(_outputFolder));

        using (var logger = new FileLogger(Configuration(_outputFolder), "generator")) {
            logger.Info("a message");
        }

        Assert.True(Directory.Exists(_outputFolder));
    }

    [Fact]
    public void RecordsLevelsAndMessages() {
        using (var logger = new FileLogger(Configuration(_outputFolder), "generator")) {
            logger.Info("an info message");
            logger.Error("an error message");
            logger.Info("with data", "the data");
        }

        var content = File.ReadAllText(Directory.GetFiles(_outputFolder).Single());

        Assert.Contains("INFO: an info message", content);
        Assert.Contains("ERROR: an error message", content);
        Assert.Contains("the data", content);
    }

    [Fact]
    public void Wrap_RecordsAnExceptionRatherThanPropagatingIt() {
        var exception = new InvalidOperationException("generator blew up");

        FileLogger.Wrap("generator", Configuration(_outputFolder), _ => throw exception);

        var content = File.ReadAllText(Directory.GetFiles(_outputFolder).Single());

        Assert.Contains("ERROR", content);
        Assert.Contains("generator blew up", content);
    }

    [Fact]
    public void Wrap_RunsTheCallbackAndDisposesTheLogger() {
        var ran = false;

        FileLogger.Wrap("generator", Configuration(_outputFolder), logger => {
            ran = true;
            logger.Info("inside");
        });

        Assert.True(ran);
        Assert.Contains("inside", File.ReadAllText(Directory.GetFiles(_outputFolder).Single()));
    }

    [Fact]
    public void AnUnwritableOutputFolder_DoesNotThrow() {
        // A path whose parent is a file cannot be created; logging must still not fail the build.
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        File.WriteAllText(file, "not a directory");

        try {
            var logger = new FileLogger(Configuration(Path.Combine(file, "nested")), "generator");
            logger.Info("a message");

            var exception = Record.Exception(() => logger.Dispose());

            Assert.Null(exception);
        }
        finally {
            File.Delete(file);
        }
    }

    private static DependencyModuleConfigurationModel Configuration(string logOutputFolder) =>
        new(
            RegistrationType.Add,
            RegisterSourceGenerator: false,
            RootNamespace: "TestNamespace",
            ProjectDir: Path.GetTempPath(),
            AutoGenerateEntry: true,
            LogOutputFolder: logOutputFolder,
            LogOutputLevel.Debug,
            GenerateFactories: false);

    public void Dispose() {
        if (Directory.Exists(_outputFolder)) {
            Directory.Delete(_outputFolder, recursive: true);
        }
    }
}

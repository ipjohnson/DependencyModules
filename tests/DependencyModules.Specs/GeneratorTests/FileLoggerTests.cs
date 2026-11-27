using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// FileLogger backs the DependencyModules_LogOutputDirectory build property. It runs inside the
/// compiler, so it must be inert unless explicitly switched on and must never fail a build.
/// </summary>
public partial class FileLoggerTests : IDisposable
{
    private readonly string _outputFolder = Path.Combine(
        Path.GetTempPath(),
        "DependencyModulesLoggerTests",
        Guid.NewGuid().ToString("n")
    );

    [VerifyTest]
    public void WithNoOutputFolder_WritesNothing()
    {
        var before = Directory.GetCurrentDirectory();
        var filesBefore = Directory.GetFiles(before);

        using (var logger = new FileLogger(Configuration(logOutputFolder: ""), "test"))
        {
            logger.Info("a message");
            logger.Error("a problem");
        }

        Verify(Directory.GetFiles(before).Length == filesBefore.Length);
    }

    /// <summary>
    /// Regression test: logs used to land in the compiler's working directory, ignoring the
    /// configured folder entirely.
    /// </summary>
    [VerifyTest]
    public void WithAnOutputFolder_WritesIntoThatFolder()
    {
        using (var logger = new FileLogger(Configuration(_outputFolder), "generator"))
        {
            logger.Info("a message");
        }

        var written = Directory.GetFiles(_outputFolder);

        var file = Single(written);
        Verify(Path.GetFileName(file).StartsWith("generator."));
        Verify(file.EndsWith(".txt"));
    }

    [VerifyTest]
    public void WithAnOutputFolder_CreatesTheFolderIfMissing()
    {
        Verify(!Directory.Exists(_outputFolder));

        using (var logger = new FileLogger(Configuration(_outputFolder), "generator"))
        {
            logger.Info("a message");
        }

        Verify(Directory.Exists(_outputFolder));
    }

    [VerifyTest]
    public void RecordsLevelsAndMessages()
    {
        using (var logger = new FileLogger(Configuration(_outputFolder), "generator"))
        {
            logger.Info("an info message");
            logger.Error("an error message");
            logger.Info("with data", "the data");
        }

        var content = File.ReadAllText(Directory.GetFiles(_outputFolder).Single());

        Verify(content.Contains("INFO: an info message"));
        Verify(content.Contains("ERROR: an error message"));
        Verify(content.Contains("the data"));
    }

    /// <summary>
    /// With no reporter the exception must escape, so Roslyn reports its own generator failure.
    /// Swallowing it produced a successful build with no registrations and no message at all.
    /// </summary>
    [VerifyTest]
    public void Wrap_WithoutAReporter_RethrowsSoTheFailureIsVisible()
    {
        var exception = ThrowsExactly<InvalidOperationException>(() =>
            FileLogger.Wrap(
                "generator",
                Configuration(_outputFolder),
                _ => throw new InvalidOperationException("generator blew up")
            )
        );

        Verify(exception.Message == "generator blew up");
    }

    [VerifyTest]
    public void Wrap_WithAReporter_HandsItTheExceptionInsteadOfPropagating()
    {
        Exception? reported = null;

        FileLogger.Wrap(
            "generator",
            Configuration(_outputFolder),
            _ => throw new InvalidOperationException("generator blew up"),
            exception => reported = exception
        );

        Verify(NotNull(reported).Message == "generator blew up");
    }

    [VerifyTest]
    public void Wrap_RecordsTheExceptionInTheLog()
    {
        FileLogger.Wrap(
            "generator",
            Configuration(_outputFolder),
            _ => throw new InvalidOperationException("generator blew up"),
            _ => { }
        );

        var content = File.ReadAllText(Directory.GetFiles(_outputFolder).Single());

        Verify(content.Contains("ERROR"));
        Verify(content.Contains("generator blew up"));
    }

    [VerifyTest]
    public void Wrap_LetsCancellationThrough()
    {
        var reported = false;

        ThrowsExactly<OperationCanceledException>(() =>
            FileLogger.Wrap(
                "generator",
                Configuration(_outputFolder),
                _ => throw new OperationCanceledException(),
                _ => reported = true
            )
        );

        Verify(!reported);
    }

    /// <summary>
    /// Loggers with one name, disposed together, usually get the same millisecond.
    /// </summary>
    [VerifyTest]
    public void LoggersWithOneName_EachWriteTheirOwnFile()
    {
        for (var i = 0; i < 10; i++)
        {
            using var logger = new FileLogger(Configuration(_outputFolder), "generator");
            logger.Info($"logger {i}");
        }

        var contents = Directory
            .GetFiles(_outputFolder)
            .Select(file => File.ReadAllText(file).Trim())
            .OrderBy(content => content, StringComparer.Ordinal);

        Verify(contents.SequenceEqual(Enumerable.Range(0, 10).Select(i => $"INFO: logger {i}")));
    }

    [VerifyTest]
    public void Wrap_RunsTheCallbackAndDisposesTheLogger()
    {
        var ran = false;

        FileLogger.Wrap(
            "generator",
            Configuration(_outputFolder),
            logger =>
            {
                ran = true;
                logger.Info("inside");
            }
        );

        Verify(ran);
        Verify(File.ReadAllText(Directory.GetFiles(_outputFolder).Single()).Contains("inside"));
    }

    [VerifyTest]
    public void AnUnwritableOutputFolder_DoesNotThrow()
    {
        // A path whose parent is a file cannot be created; logging must still not fail the build.
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        File.WriteAllText(file, "not a directory");

        try
        {
            var logger = new FileLogger(Configuration(Path.Combine(file, "nested")), "generator");
            logger.Info("a message");

            Exception? exception = null;

            try
            {
                logger.Dispose();
            }
            catch (Exception caught)
            {
                exception = caught;
            }

            Verify(exception is null);
        }
        finally
        {
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
            GenerateFactories: false
        );

    public void Dispose()
    {
        if (Directory.Exists(_outputFolder))
        {
            Directory.Delete(_outputFolder, recursive: true);
        }
    }
}

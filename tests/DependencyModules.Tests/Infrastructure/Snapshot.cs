using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// Minimal approval-test helper: compares text against a committed snapshot file so that
/// unintended changes to generated output show up as a reviewable diff.
///
/// To accept new output, re-run the tests with UPDATE_SNAPSHOTS=1:
///     UPDATE_SNAPSHOTS=1 dotnet test tests/DependencyModules.Tests
/// and review the resulting changes under tests/DependencyModules.Tests/Snapshots.
///
/// Snapshots are read from the build output, where they are copied by the project file.
/// [CallerFilePath] is deliberately not used to locate them: deterministic builds
/// (ContinuousIntegrationBuild) rewrite source paths to /_/..., which does not exist on disk.
/// </summary>
public static class Snapshot {
    private const string UpdateVariable = "UPDATE_SNAPSHOTS";
    private const string SnapshotFolderName = "Snapshots";

    /// <summary>
    /// The copy of the snapshots in the build output. Always present, on any machine.
    /// </summary>
    private static string OutputSnapshotDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, SnapshotFolderName);

    /// <summary>
    /// The snapshots in the source tree, injected by the project file at build time. Only used
    /// when updating, and only meaningful when the tests run on the machine that built them.
    /// </summary>
    private static string? SourceSnapshotDirectory { get; } =
        typeof(Snapshot).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "SnapshotDirectory")
            ?.Value;

    public static void Match(
        string actual,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "") {

        var testClass = Path.GetFileNameWithoutExtension(callerFilePath);
        var fileName = $"{testClass}.{callerMemberName}.verified.txt";
        var normalized = Normalize(actual);

        if (ShouldUpdate) {
            Update(fileName, normalized);
            return;
        }

        var snapshotPath = Path.Combine(OutputSnapshotDirectory, fileName);

        Assert.True(File.Exists(snapshotPath),
            $"Missing snapshot '{fileName}'. Re-run with {UpdateVariable}=1 to create it." +
            Environment.NewLine + "Actual output was:" + Environment.NewLine + normalized);

        var expected = Normalize(File.ReadAllText(snapshotPath));

        if (expected != normalized) {
            var receivedPath = Path.Combine(OutputSnapshotDirectory, fileName.Replace(".verified.txt", ".received.txt"));
            File.WriteAllText(receivedPath, normalized);

            Assert.Fail(
                $"Generated output does not match '{fileName}'." + Environment.NewLine +
                $"Wrote actual output to '{receivedPath}'." + Environment.NewLine +
                $"If the change is intended, re-run with {UpdateVariable}=1." + Environment.NewLine +
                Environment.NewLine + FirstDifference(expected, normalized));
        }
    }

    /// <summary>
    /// Writes to the source tree so the change can be reviewed and committed, and to the build
    /// output so a subsequent run in the same session reads the updated value.
    /// </summary>
    private static void Update(string fileName, string content) {
        Assert.True(!string.IsNullOrEmpty(SourceSnapshotDirectory),
            $"Cannot update snapshots: the assembly has no SnapshotDirectory metadata. " +
            $"Rebuild the test project, then re-run with {UpdateVariable}=1.");

        Assert.True(Directory.Exists(Path.GetDirectoryName(SourceSnapshotDirectory!)),
            $"Cannot update snapshots: '{SourceSnapshotDirectory}' is not reachable from this machine. " +
            "Snapshots can only be updated from a checkout of the source tree.");

        Directory.CreateDirectory(SourceSnapshotDirectory!);
        File.WriteAllText(Path.Combine(SourceSnapshotDirectory!, fileName), content);

        Directory.CreateDirectory(OutputSnapshotDirectory);
        File.WriteAllText(Path.Combine(OutputSnapshotDirectory, fileName), content);
    }

    private static bool ShouldUpdate {
        get {
            var value = Environment.GetEnvironmentVariable(UpdateVariable);
            return !string.IsNullOrEmpty(value) && !value.Equals("0", StringComparison.Ordinal) &&
                   !value.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string FirstDifference(string expected, string actual) {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++) {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
            var actualLine = i < actualLines.Length ? actualLines[i] : "<end of file>";

            if (expectedLine != actualLine) {
                return $"First difference at line {i + 1}:" + Environment.NewLine +
                       $"  expected: {expectedLine}" + Environment.NewLine +
                       $"  actual:   {actualLine}";
            }
        }

        return string.Empty;
    }
}

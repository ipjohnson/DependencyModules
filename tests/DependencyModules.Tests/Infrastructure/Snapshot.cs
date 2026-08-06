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
/// </summary>
public static class Snapshot {
    private const string UpdateVariable = "UPDATE_SNAPSHOTS";

    public static void Match(
        string actual,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "") {

        var testClass = Path.GetFileNameWithoutExtension(callerFilePath);
        var snapshotDirectory = Path.Combine(FindTestProjectRoot(callerFilePath), "Snapshots");
        var snapshotPath = Path.Combine(snapshotDirectory, $"{testClass}.{callerMemberName}.verified.txt");

        var normalized = Normalize(actual);

        if (ShouldUpdate) {
            Directory.CreateDirectory(snapshotDirectory);
            File.WriteAllText(snapshotPath, normalized);
            return;
        }

        Assert.True(File.Exists(snapshotPath),
            $"Missing snapshot '{Path.GetFileName(snapshotPath)}'. Re-run with {UpdateVariable}=1 to create it." +
            Environment.NewLine + "Actual output was:" + Environment.NewLine + normalized);

        var expected = Normalize(File.ReadAllText(snapshotPath));

        if (expected != normalized) {
            var receivedPath = snapshotPath.Replace(".verified.txt", ".received.txt");
            File.WriteAllText(receivedPath, normalized);

            Assert.Fail(
                $"Generated output does not match '{Path.GetFileName(snapshotPath)}'." + Environment.NewLine +
                $"Wrote actual output to '{Path.GetFileName(receivedPath)}'." + Environment.NewLine +
                $"If the change is intended, re-run with {UpdateVariable}=1." + Environment.NewLine +
                Environment.NewLine + FirstDifference(expected, normalized));
        }
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

    /// <summary>
    /// Walks up from the test source file to the directory holding the .csproj, so snapshots are
    /// written next to the sources rather than into the build output.
    /// </summary>
    private static string FindTestProjectRoot(string callerFilePath) {
        var directory = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);

        while (directory != null && !directory.EnumerateFiles("*.csproj").Any()) {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException($"Could not locate the test project root from '{callerFilePath}'.");
    }

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

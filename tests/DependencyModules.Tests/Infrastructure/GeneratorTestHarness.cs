extern alias ConventionsGen;

using System.Collections.Immutable;
using System.Reflection;
using DependencyModules.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// Runs the DependencyModules source generator over an in-memory compilation so tests can assert
/// on the code it produces without going through a real build.
/// </summary>
public static class GeneratorTestHarness {
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(BuildReferences);

    /// <summary>
    /// Compiles <paramref name="sources"/>, runs the generator, and returns everything it emitted.
    /// </summary>
    /// <param name="sources">Source files keyed by file name. File names matter: the generator
    /// treats <c>Program.cs</c> specially when auto-generating an application module.</param>
    /// <param name="buildProperties">MSBuild properties visible to the generator, without the
    /// <c>build_property.</c> prefix.</param>
    /// <param name="withConventions">
    /// Also runs the convention generator, which ships as its own analyzer. Opt-in rather than
    /// always on, because it adds its contract types to every compilation through post-initialization
    /// output and would otherwise change the output of every existing snapshot test.
    /// </param>
    public static GeneratorResult Run(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        string assemblyName = "GeneratorTestAssembly",
        bool withConventions = false) {

        // MSBuild hands the compiler absolute paths, and the generator compares a file's location
        // against ProjectDir to decide whether it owns the auto-generated ApplicationModule.
        // Rooting the test sources under ProjectDir keeps that comparison meaningful.
        var projectDir = ResolveProjectDir(buildProperties);

        var syntaxTrees = sources
            .Select(pair => CSharpSyntaxTree.ParseText(
                pair.Value,
                new CSharpParseOptions(LanguageVersion.Latest),
                path: Path.Combine(projectDir, pair.Key)))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            References.Value,
            new CSharpCompilationOptions(outputKind, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            Generators(withConventions),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();

        var generatedSources = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .ToDictionary(
                generated => generated.HintName,
                generated => generated.SourceText.ToString());

        // The generator catches its own exceptions and, with no log folder configured, discards
        // them. Surface them here so a crashing generator fails loudly instead of producing nothing.
        var exceptions = runResult.Results
            .Select(result => result.Exception)
            .Where(exception => exception != null)
            .ToArray();

        return new GeneratorResult(
            generatedSources,
            generatorDiagnostics,
            outputCompilation.GetDiagnostics(),
            outputCompilation,
            exceptions!);
    }

    /// <summary>
    /// Convenience overload for the common single-file case.
    /// </summary>
    public static GeneratorResult Run(
        string source,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        bool withConventions = false) =>
        Run(new Dictionary<string, string> { ["Test.cs"] = source }, buildProperties,
            withConventions: withConventions);

    private static ISourceGenerator[] Generators(bool withConventions) =>
        withConventions
            ? new ISourceGenerator[] {
                new SourceGenerator.SourceGenerator().AsSourceGenerator(),
                new ConventionsGen::DependencyModules.Conventions.ConventionSourceGenerator().AsSourceGenerator(),
            }
            : new ISourceGenerator[] {
                new SourceGenerator.SourceGenerator().AsSourceGenerator(),
            };

    /// <summary>
    /// Runs the generator over <paramref name="first"/>, then re-runs the same driver over
    /// <paramref name="second"/>, reporting why each output was or was not recomputed.
    ///
    /// This is how the model comparers earn their keep: when an edit cannot affect generated
    /// output, Roslyn should reuse the cached result rather than regenerate. Getting this wrong
    /// makes the IDE recompute on every keystroke, or serve stale output after a real change.
    /// </summary>
    public static IncrementalRunResult RunIncremental(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        bool withConventions = false) {

        var projectDir = ResolveProjectDir(buildProperties);

        Compilation Compile(IReadOnlyDictionary<string, string> sources) =>
            CSharpCompilation.Create(
                "GeneratorTestAssembly",
                sources.Select(pair => CSharpSyntaxTree.ParseText(
                    pair.Value,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: Path.Combine(projectDir, pair.Key))),
                References.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            Generators(withConventions),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(Compile(first));
        var firstOutputs = Outputs(driver.GetRunResult());

        driver = driver.RunGenerators(Compile(second));
        var secondRun = driver.GetRunResult();

        var reasons = secondRun.Results
            .SelectMany(result => result.TrackedOutputSteps)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();

        return new IncrementalRunResult(firstOutputs, Outputs(secondRun), reasons);
    }

    private static IReadOnlyDictionary<string, string> Outputs(GeneratorDriverRunResult runResult) =>
        runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .ToDictionary(generated => generated.HintName, generated => generated.SourceText.ToString());

    internal static string DefaultProjectDir { get; } =
        Path.Combine(Path.GetTempPath(), "GeneratorTest") + Path.DirectorySeparatorChar;

    private static string ResolveProjectDir(IReadOnlyDictionary<string, string>? buildProperties) =>
        buildProperties != null && buildProperties.TryGetValue("ProjectDir", out var configured)
            ? configured
            : DefaultProjectDir;

    private static ImmutableArray<MetadataReference> BuildReferences() {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        // The framework reference set the test host was resolved against.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted) {
            foreach (var path in trusted.Split(Path.PathSeparator)) {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) {
                    builder.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        // The assemblies the generated code actually binds against.
        foreach (var assembly in new[] {
                     typeof(DependencyModuleAttribute).Assembly,
                     typeof(IServiceCollection).Assembly,
                     typeof(ServiceCollection).Assembly
                 }) {
            AddAssembly(builder, assembly);
        }

        return builder.ToImmutable();
    }

    private static void AddAssembly(ImmutableArray<MetadataReference>.Builder builder, Assembly assembly) {
        if (string.IsNullOrEmpty(assembly.Location)) {
            return;
        }

        var alreadyPresent = builder.Any(reference =>
            string.Equals(reference.Display, assembly.Location, StringComparison.OrdinalIgnoreCase));

        if (!alreadyPresent) {
            builder.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
    }
}

/// <summary>
/// Everything a generator run produced: the generated files plus diagnostics from both the
/// generator itself and the compilation that includes its output.
/// </summary>
public class GeneratorResult(
    IReadOnlyDictionary<string, string> generatedSources,
    ImmutableArray<Diagnostic> generatorDiagnostics,
    ImmutableArray<Diagnostic> compilationDiagnostics,
    Compilation compilation,
    IReadOnlyList<Exception> generatorExceptions) {

    public IReadOnlyDictionary<string, string> GeneratedSources { get; } = generatedSources;

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; } = generatorDiagnostics;

    public ImmutableArray<Diagnostic> CompilationDiagnostics { get; } = compilationDiagnostics;

    public Compilation Compilation { get; } = compilation;

    public IReadOnlyList<Exception> GeneratorExceptions { get; } = generatorExceptions;

    public IEnumerable<Diagnostic> Errors =>
        GeneratorDiagnostics.Concat(CompilationDiagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// The single generated file whose hint name contains <paramref name="fragment"/>.
    /// </summary>
    public string SourceContaining(string fragment) {
        var matches = GeneratedSources
            .Where(pair => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(matches.Length > 0,
            $"No generated file matched '{fragment}'. Generated: {string.Join(", ", GeneratedSources.Keys)}");
        Assert.True(matches.Length == 1,
            $"'{fragment}' matched more than one generated file: {string.Join(", ", matches.Select(m => m.Key))}");

        return matches[0].Value;
    }

    /// <summary>
    /// Asserts the generator produced output that compiles cleanly.
    /// </summary>
    public GeneratorResult AssertNoErrors() {
        Assert.True(GeneratorExceptions.Count == 0,
            "The generator threw:" + Environment.NewLine +
            string.Join(Environment.NewLine, GeneratorExceptions.Select(e => $"  {e}")));

        var errors = Errors.ToArray();

        Assert.True(errors.Length == 0,
            "Expected no errors, got:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => $"  {e.Id} {e.GetMessage()} @ {e.Location.GetLineSpan()}")));

        return this;
    }

    /// <summary>
    /// All generated files concatenated in a stable order, suitable for snapshotting.
    /// </summary>
    public string ToSnapshot() {
        var builder = new System.Text.StringBuilder();

        foreach (var pair in GeneratedSources.OrderBy(p => p.Key, StringComparer.Ordinal)) {
            builder.AppendLine($"// ---- {pair.Key} ----");
            builder.AppendLine(pair.Value.Replace("\r\n", "\n").TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
    }
}

/// <summary>
/// The generated output of two consecutive generator runs, plus why the second run's outputs
/// were recomputed or reused.
/// </summary>
public class IncrementalRunResult(
    IReadOnlyDictionary<string, string> firstRun,
    IReadOnlyDictionary<string, string> secondRun,
    IReadOnlyList<IncrementalStepRunReason> outputReasons) {

    public IReadOnlyDictionary<string, string> FirstRun { get; } = firstRun;

    public IReadOnlyDictionary<string, string> SecondRun { get; } = secondRun;

    public IReadOnlyList<IncrementalStepRunReason> OutputReasons { get; } = outputReasons;

    /// <summary>
    /// True when the second run reused every cached output, meaning the edit was correctly
    /// recognised as irrelevant to generation.
    /// </summary>
    public bool AllOutputsCached =>
        OutputReasons.Count > 0 &&
        OutputReasons.All(reason =>
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
}

internal class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string>? buildProperties)
    : AnalyzerConfigOptionsProvider {

    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(buildProperties);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal class TestAnalyzerConfigOptions : AnalyzerConfigOptions {
    private readonly Dictionary<string, string> _options;

    public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string>? buildProperties) {
        _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["build_property.RootNamespace"] = "TestNamespace",
            ["build_property.ProjectDir"] = GeneratorTestHarness.DefaultProjectDir
        };

        if (buildProperties != null) {
            foreach (var pair in buildProperties) {
                _options["build_property." + pair.Key] = pair.Value;
            }
        }
    }

    public override bool TryGetValue(string key, out string value) => _options.TryGetValue(key, out value!);
}

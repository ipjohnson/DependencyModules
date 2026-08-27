
using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    /// <param name="generators">The generators to run. Defaults to the one this package ships;
    /// the extension-seam tests pass a framework-shaped generator of their own instead.</param>
    public static GeneratorResult Run(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        string assemblyName = "GeneratorTestAssembly",
        IReadOnlyList<MetadataReference>? additionalReferences = null,
        IReadOnlyList<ISourceGenerator>? generators = null) {

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
            additionalReferences == null
                ? References.Value
                : References.Value.Concat(additionalReferences),
            new CSharpCompilationOptions(outputKind, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            generators == null ? Generators() : generators.ToArray(),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();

        // Hint names are unique within a generator but not across them, so a run with more than one
        // generator can produce the same name twice. Keyed rather than grouped it threw, hiding the
        // duplication behind a dictionary error; two generators emitting one type's partial twice
        // is a real defect, so it is recorded and asserted on instead.
        var emitted = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => (generated.HintName, Source: generated.SourceText.ToString()))
            .ToArray();

        var duplicateHintNames = emitted
            .GroupBy(generated => generated.HintName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        var generatedSources = emitted
            .GroupBy(generated => generated.HintName)
            .ToDictionary(group => group.Key, group => group.First().Source);

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
            exceptions!,
            duplicateHintNames);
    }

    /// <summary>
    /// Convenience overload for the common single-file case.
    /// </summary>
    public static GeneratorResult Run(
        string source,
        IReadOnlyDictionary<string, string>? buildProperties = null) =>
        Run(new Dictionary<string, string> { ["Test.cs"] = source }, buildProperties);

    /// <summary>
    /// One generator. Conventions, services, decorators and interception all come from it.
    /// </summary>
    /// <remarks>
    /// Conventions used to ship as a second analyzer and be opt-in here, so its contract types did
    /// not land in every compilation. They are declared in DependencyModules.Runtime now, and the
    /// generator that reads them is part of this one — which is what lets a decoration be emitted
    /// closed over a registration a convention produced.
    /// </remarks>
    private static ISourceGenerator[] Generators() =>
        new ISourceGenerator[] {
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
            Generators(),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(Compile(first));
        var firstOutputs = Outputs(driver.GetRunResult());

        driver = driver.RunGenerators(Compile(second));
        var secondRun = driver.GetRunResult();

        var outputs = secondRun.Results
            .SelectMany(result => result.TrackedOutputSteps)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => (output.Reason, EmittedSource: EmittedSourceCount(output.Value)))
            .ToArray();

        return new IncrementalRunResult(firstOutputs, Outputs(secondRun), outputs);
    }

    /// <summary>
    /// How many files a tracked source output produced.
    /// </summary>
    /// <remarks>
    /// Each one carries a <c>(sources, diagnostics)</c> pair, which is what tells an output that
    /// emits from one that only reports. The distinction matters: a diagnostics-only output is
    /// combined with the compilation on purpose, so it re-runs whenever anything is typed, and
    /// counting that as a cache miss would say the generator regenerates on every keystroke when it
    /// does not.
    ///
    /// Read through ITuple rather than by casting: the element type is not public.
    /// </remarks>
    private static int EmittedSourceCount(object? value) =>
        value is ITuple { Length: 2 } tuple && tuple[0] is ICollection sources ? sources.Count : 0;

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

    /// <summary>
    /// Compiles a standalone library and returns a reference to it, plus the loaded assembly.
    /// </summary>
    /// <remarks>
    /// The only honest way to test scanning a referenced assembly: the types have to live in real
    /// metadata with no syntax tree in the consuming compilation, which is exactly the situation
    /// InAssemblyOf exists for. Loading it as well lets a behavioural test resolve the services the
    /// generated code registers.
    /// </remarks>
    /// <param name="runGenerator">
    /// Whether to run the generator over the library before emitting it. A package built the normal
    /// way carries what the generator wrote for it — a module's attribute above all — and a consumer
    /// sees that in metadata. Off by default because most callers only need plain types to scan.
    /// </param>
    public static (MetadataReference Reference, System.Reflection.Assembly Assembly) CompileLibrary(
        string source, string assemblyName, bool runGenerator = false) {

        Compilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        if (runGenerator) {
            CSharpGeneratorDriver.Create(
                    Generators(),
                    optionsProvider: new TestAnalyzerConfigOptionsProvider(null),
                    parseOptions: new CSharpParseOptions(LanguageVersion.Latest))
                .RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _);
        }

        using var stream = new MemoryStream();

        var result = compilation.Emit(stream);

        Xunit.Assert.True(result.Success,
            "The test library did not compile: " + string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var bytes = stream.ToArray();
        var assembly = System.Reflection.Assembly.Load(bytes);

        // Assembly.Load(byte[]) puts it in the default context but does not make it discoverable by
        // name, so generated code referencing it fails to bind at run time. The resolver closes that.
        lock (LoadedLibraries) {
            LoadedLibraries[assemblyName] = assembly;

            if (!_resolverHooked) {
                System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => {
                    lock (LoadedLibraries) {
                        return name.Name != null && LoadedLibraries.TryGetValue(name.Name, out var found)
                            ? found
                            : null;
                    }
                };

                _resolverHooked = true;
            }
        }

        return (MetadataReference.CreateFromImage(bytes), assembly);
    }

    private static readonly Dictionary<string, System.Reflection.Assembly> LoadedLibraries = new();

    private static bool _resolverHooked;

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
    IReadOnlyList<Exception> generatorExceptions,
    IReadOnlyList<string>? duplicateHintNames = null) {

    public IReadOnlyDictionary<string, string> GeneratedSources { get; } = generatedSources;

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; } = generatorDiagnostics;

    public ImmutableArray<Diagnostic> CompilationDiagnostics { get; } = compilationDiagnostics;

    public Compilation Compilation { get; } = compilation;

    public IReadOnlyList<Exception> GeneratorExceptions { get; } = generatorExceptions;

    /// <summary>
    /// Hint names emitted by more than one generator in the same run.
    /// </summary>
    public IReadOnlyList<string> DuplicateHintNames { get; } = duplicateHintNames ?? Array.Empty<string>();

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
    IReadOnlyList<(IncrementalStepRunReason Reason, int EmittedSource)> outputs) {

    public IReadOnlyDictionary<string, string> FirstRun { get; } = firstRun;

    public IReadOnlyDictionary<string, string> SecondRun { get; } = secondRun;

    public IReadOnlyList<IncrementalStepRunReason> OutputReasons { get; } =
        outputs.Select(output => output.Reason).ToArray();

    /// <summary>
    /// True when the second run reused every output that produced a file, meaning the edit was
    /// correctly recognised as irrelevant to generation.
    /// </summary>
    /// <remarks>
    /// Outputs that emit nothing are excluded, and the exclusion is the point rather than a
    /// loophole. Diagnostics are reported from their own outputs, combined with the compilation so
    /// that a location can carry the syntax tree Roslyn needs before .editorconfig or #pragma can
    /// silence it. The compilation changes on every keystroke, so those outputs re-run on every
    /// keystroke by design - they write no source, and holding them to a cache that cannot apply
    /// would report a regression that is not there. What must stay cached is emission, and that is
    /// what this measures.
    /// </remarks>
    public bool AllOutputsCached =>
        outputs.Any(output => output.EmittedSource > 0) &&
        outputs
            .Where(output => output.EmittedSource > 0)
            .All(output => output.Reason
                is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
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

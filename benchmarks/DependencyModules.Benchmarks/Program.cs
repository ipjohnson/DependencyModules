using System.Diagnostics;
using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Benchmarks;

/// <summary>
/// Times the convention generator over synthetic compilations.
/// </summary>
/// <remarks>
/// <para>
/// Run it with tiered compilation off, or the numbers drift as the JIT warms up across the sweep:
/// </para>
/// <code>
/// DOTNET_TieredCompilation=0 dotnet run -c Release --project benchmarks/DependencyModules.Benchmarks
/// </code>
/// <para>
/// Four things here are load-bearing, each because getting it wrong produced a believable but wrong
/// number while this was being written:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Time only RunGeneratorsAndUpdateCompilation.</b> The test harness also calls GetDiagnostics on
/// the output compilation, which is a full semantic analysis and buries everything else.
/// </item>
/// <item>
/// <b>One syntax tree per class.</b> Roslyn re-runs the predicate and transform for every node in a
/// tree that changed and serves the rest from cache, so a single-file compilation makes every
/// keystroke look like a cold build.
/// </item>
/// <item>
/// <b>Real class bodies.</b> The transform's cost tracks how much code a class contains, not how
/// many classes there are. One-line classes understated it by roughly five times.
/// </item>
/// <item>
/// <b>The incremental row is the one that matters.</b> A cold build pays once; an IDE re-runs the
/// generator on every keystroke.
/// </item>
/// </list>
/// </remarks>
public static class Program {

    private const int Runs = 15;

    public static void Main() {
        Console.WriteLine(
            $"TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "(default, results will drift)"}");

        for (var i = 0; i < 20; i++) {
            ColdRun(BuildSources(400, 100));
        }

        Console.WriteLine();
        Console.WriteLine("classes  implementing   cold ms   after one edit ms");
        Console.WriteLine("---------------------------------------------------");

        foreach (var total in new[] { 500, 2000 }) {
            foreach (var implementing in new[] { 0, total / 4, total }) {
                var cold = Median(() => ColdRun(BuildSources(total, implementing)));
                var incremental = Median(() => IncrementalRun(total, implementing));

                Console.WriteLine($"{total,7}  {implementing,12}  {cold,8:F1}  {incremental,18:F1}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Frameworks stacked on the extension seam, 2000 classes:");
        FrameworkStack(2000);
    }

    private static double Median(Func<double> measure) {
        var timings = new List<double>(Runs);

        for (var run = 0; run < Runs; run++) {
            timings.Add(measure());
        }

        timings.Sort();

        return timings[timings.Count / 2];
    }

    /// <summary>
    /// What a framework building on this library adds: its own module attribute as the entry point,
    /// and its own attributes collected through the shared indexed provider.
    /// </summary>
    /// <remarks>
    /// Third parties compile in the Impl source package and declare a generator of this shape, so
    /// what a consuming project loads is this one plus one per framework. Measured rather than
    /// assumed, because it is the cost every such framework imposes on every build.
    /// </remarks>
    private class ExtensionShapedGenerator(string attributeName) : BaseSourceGenerator {

        protected override ITypeDefinition[] ModuleAttributeTypes() =>
            new[] { TypeDefinition.Get("Bench.Framework", attributeName) };

        protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
            yield return new FrameworkAttributeGenerator();
        }
    }

    private class FrameworkAttributeGenerator : IDependencyModuleSourceGenerator {

        private static readonly ITypeDefinition[] _attributes = {
            TypeDefinition.Get("Bench.Framework", "EndpointAttribute"),
            TypeDefinition.Get("Bench.Framework", "HandlerAttribute"),
            TypeDefinition.Get("Bench.Framework", "JobAttribute"),
        };

        public void SetupGenerator(
            IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {

            var models = AttributeModelCollector.Collect(
                context,
                _attributes,
                static (syntaxContext, cancellation) =>
                    ServiceModelUtility.GetServiceModel(syntaxContext, cancellation) ?? ServiceModel.Ignore,
                new ServiceModelComparer(),
                ServiceModel.Ignore);

            context.RegisterSourceOutput(
                incrementalValueProvider.Collect().Combine(models),
                static (productionContext, data) => { });
        }
    }

    private static void FrameworkStack(int total) {
        Console.WriteLine();
        Console.WriteLine("analyzers loaded                          cold ms   after one edit ms");
        Console.WriteLine("--------------------------------------------------------------------");

        for (var frameworks = 0; frameworks <= 3; frameworks++) {
            var count = frameworks;

            var cold = Median(() => Run(BuildSources(total, total / 4), count, cold: true));
            var incremental = Median(() => Run(BuildSources(total, total / 4), count, cold: false));

            var label = count == 0
                ? "DependencyModules only"
                : $"DependencyModules + {count} framework generator(s)";

            Console.WriteLine($"{label,-40} {cold,8:F1} {incremental,18:F1}");
        }
    }

    private static double Run(string[] sources, int frameworks, bool cold) {
        var compilation = Compile(sources);

        var generators = new List<ISourceGenerator> {
            new SourceGenerator.SourceGenerator().AsSourceGenerator()
        };

        for (var i = 0; i < frameworks; i++) {
            generators.Add(new ExtensionShapedGenerator($"Framework{i}Attribute").AsSourceGenerator());
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);

        if (cold) {
            return Time(() => driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _));
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var options = new CSharpParseOptions(LanguageVersion.Latest);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.ElementAt(1),
            CSharpSyntaxTree.ParseText(sources[1].Replace("_seed = 0", "_seed = 42"), options));

        return Time(() => driver.RunGeneratorsAndUpdateCompilation(edited, out _, out _));
    }

    private static double ColdRun(string[] sources) {
        var compilation = Compile(sources);

        // A fresh driver each run: reusing one would measure the incremental cache instead.
        var driver = CSharpGeneratorDriver.Create(
            new SourceGenerator.SourceGenerator().AsSourceGenerator());

        return Time(() => driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _));
    }

    /// <summary>
    /// The second run of one driver, after editing a method body in a single file.
    /// </summary>
    private static double IncrementalRun(int total, int implementing) {
        var sources = BuildSources(total, implementing);
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = Compile(sources);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new SourceGenerator.SourceGenerator().AsSourceGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        // Only the edited tree is replaced; the other N-1 keep their identity and should stay cached.
        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.ElementAt(1),
            CSharpSyntaxTree.ParseText(sources[1].Replace("_seed = 0", "_seed = 42"), options));

        return Time(() => driver.RunGeneratorsAndUpdateCompilation(edited, out _, out _));
    }

    private static double Time(Action action) {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var start = Stopwatch.GetTimestamp();

        action();

        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static CSharpCompilation Compile(string[] sources) {
        var options = new CSharpParseOptions(LanguageVersion.Latest);

        return CSharpCompilation.Create(
            "BenchAssembly",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, options)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string[] BuildSources(int total, int implementing) {
        var trees = new string[total + 2];

        trees[0] = "namespace BenchNamespace; public interface IMarker { }";

        for (var i = 0; i < total; i++) {
            trees[i + 1] = i < implementing
                ? $"namespace BenchNamespace; public class Implementing{i} : IMarker {{{Body(i)}}}"
                : $"namespace BenchNamespace; public class Plain{i} {{{Body(i)}}}";
        }

        var builder = new StringBuilder();

        builder.AppendLine("using DependencyModules.Runtime.Attributes;");
        builder.AppendLine("using DependencyModules.Runtime.Conventions;");
        builder.AppendLine("namespace BenchNamespace;");
        builder.AppendLine("[DependencyModule]");
        builder.AppendLine("public partial class BenchModule : IConventionModule {");
        builder.AppendLine("    void IConventionModule.Conventions(IConventionDefinitions conventions) {");
        builder.AppendLine("        conventions.RegisterAll<IMarker>().AsScoped();");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        trees[total + 1] = builder.ToString();

        return trees;
    }

    private static string Body(int i) => $$"""

        private readonly int _seed = {{i}};
        public int Value => _seed;
        public int Add(int a, int b) { var t = a + b; if (t > 10) { t -= 1; } return t + _seed; }
        public string Describe() { var parts = new[] { "a", "b", "c" }; return string.Join(",", parts) + _seed; }
        public bool Check(int x) { for (var j = 0; j < x; j++) { if (j == _seed) { return true; } } return false; }
        public int Sum(int[] values) { var total = 0; foreach (var v in values) { total += v; } return total; }
""";

    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences() {
        // Touched so the runtime and DI assemblies are loaded before the sweep below.
        _ = typeof(IServiceCollection);
        _ = typeof(Runtime.ModuleEnvironment);

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();
    }
}

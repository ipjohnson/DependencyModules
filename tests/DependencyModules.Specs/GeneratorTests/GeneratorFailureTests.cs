using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Utilities;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// An exception inside the generator is reported as DM0001. An exception that escapes an output
/// gives CS8785, which is only a warning, and Roslyn then drops every file the generator wrote in
/// that run.
/// </summary>
public partial class GeneratorFailureTests : IDisposable
{
    private readonly string _logFolder = Path.Combine(
        Path.GetTempPath(),
        "DependencyModulesFailureTests",
        Guid.NewGuid().ToString("n")
    );

    [VerifyTest]
    public void AModuleThatCannotBeWritten_IsReported_AndTheOtherModulesAreWritten()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = "namespace TestNamespace;" },
            generators: new[] { new ModuleWriterGenerator(_logFolder).AsSourceGenerator() }
        );

        Verify(result.GeneratorExceptions.Count == 0);

        var failure = Single(result.GeneratorDiagnostics, d => d.Id == "DM0001");
        Verify(failure.GetMessage().Contains("hintName"));

        Verify(result.GeneratedSources.ContainsKey("GoodModule.Module.g.cs"));

        var log = Directory.GetFiles(_logFolder, "DependencyModuleWriter.*.txt");
        Verify(log.Any(file => File.ReadAllText(file).Contains("Bad\"Module")));
    }

    [VerifyTest]
    public void AnOutputThatOnlyReports_IsReported_AndTheGeneratedFilesAreKept()
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = "namespace TestNamespace;" },
            generators: new[] { new ThrowingReportGenerator().AsSourceGenerator() }
        );

        Verify(result.GeneratorExceptions.Count == 0);
        Verify(
            result.GeneratorDiagnostics.Any(d =>
                d.Id == "DM0001" && d.GetMessage().Contains("the report failed")
            )
        );
        Verify(result.GeneratedSources.ContainsKey("Kept.g.cs"));
    }

    /// <summary>
    /// The code output and the diagnostics output of the service stage each write a log. The
    /// configuration and the services are in the log of the code output, and no other log replaces
    /// it.
    /// </summary>
    [VerifyTest]
    public void TheServiceLogKeepsTheConfigurationAndTheServices()
    {
        GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IClock { }

            [SingletonService]
            public class Clock : IClock { }

            [DependencyModule]
            public partial class ShopModule;
            """,
            new Dictionary<string, string> { ["DependencyModules_LogOutputDirectory"] = _logFolder }
        );

        var names = Directory.GetFiles(_logFolder).Select(Path.GetFileName).ToArray();

        var log = File.ReadAllText(
            Path.Combine(
                _logFolder,
                Single(
                    names,
                    name =>
                        name!.StartsWith("ServiceSourceGenerator.")
                        && !name.StartsWith("ServiceSourceGenerator.Diagnostics.")
                )!
            )
        );

        Verify(log.Contains("Configuration:"));
        Verify(log.Contains("Clock -> IClock"));
        Single(names, name => name!.StartsWith("ServiceSourceGenerator.Diagnostics."));
    }

    /// <summary>
    /// Runs only the module writer, over one module whose name Roslyn refuses as a hint name and
    /// one module that is correct.
    /// </summary>
    private class ModuleWriterGenerator(string logFolder) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var configuration = ModelFactory.Configuration(logOutputFolder: logFolder);

            var models = context.CompilationProvider.Select(
                (_, _) =>
                    ImmutableArray.Create(
                        (
                            ModelFactory.EntryPoint(
                                entryPointType: TypeDefinition.Get("TestNamespace", "Bad\"Module")
                            ),
                            configuration
                        ),
                        (
                            ModelFactory.EntryPoint(
                                entryPointType: TypeDefinition.Get("TestNamespace", "GoodModule")
                            ),
                            configuration
                        )
                    )
            );

            DependencyModuleWriter.Register(context, models, generateAttribute: false);
        }
    }

    private class ThrowingReportGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (productionContext, _) => productionContext.AddSource("Kept.g.cs", "// kept")
            );

            context.RegisterSourceOutput(
                context.CompilationProvider,
                (productionContext, _) =>
                    FileLogger.Wrap(
                        "Report",
                        ModelFactory.Configuration(),
                        productionContext,
                        _ => throw new InvalidOperationException("the report failed")
                    )
            );
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_logFolder))
        {
            Directory.Delete(_logFolder, recursive: true);
        }
    }
}

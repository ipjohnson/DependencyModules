using System.Collections.Immutable;
using DependencyModules.CodeFixes;
using DependencyModules.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// Runs <see cref="ServiceTypeAnalyzer"/> over a workspace and applies
/// <see cref="ServiceTypeCodeFixProvider"/> the way an IDE does.
/// </summary>
public static class CodeFixHarness
{
    public const string DiagnosticId = "DM0026";

    /// <summary>
    /// A solution with one project per entry, each holding its sources in order as
    /// <c>Test0.cs</c>, <c>Test1.cs</c> and so on.
    /// </summary>
    /// <param name="severity">The severity DM0026 is set to, or null to leave its default.</param>
    public static Solution CreateSolution(
        IReadOnlyList<IReadOnlyList<string>> projects,
        ReportDiagnostic? severity = ReportDiagnostic.Warn
    )
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        );

        if (severity != null)
        {
            options = options.WithSpecificDiagnosticOptions(
                ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                    DiagnosticId,
                    severity.Value
                )
            );
        }

        for (var projectIndex = 0; projectIndex < projects.Count; projectIndex++)
        {
            var projectId = ProjectId.CreateNewId();

            solution = solution
                .AddProject(projectId, $"Project{projectIndex}", $"Project{projectIndex}", "C#")
                .WithProjectCompilationOptions(projectId, options)
                .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Latest))
                .AddMetadataReferences(projectId, GeneratorTestHarness.MetadataReferences);

            for (var index = 0; index < projects[projectIndex].Count; index++)
            {
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectId),
                    $"Test{index}.cs",
                    projects[projectIndex][index]
                );
            }
        }

        return solution;
    }

    public static Document CreateDocument(string source) =>
        CreateSolution(new[] { new[] { source } }).Projects.Single().Documents.Single();

    /// <summary>
    /// The DM0026 diagnostics in a project, in source order.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(Project project)
    {
        var compilation = await project.GetCompilationAsync();

        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ServiceTypeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticId)
            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    /// <summary>
    /// The code actions offered for one diagnostic.
    /// </summary>
    public static async Task<IReadOnlyList<CodeAction>> ActionsAsync(
        Document document,
        Diagnostic diagnostic
    )
    {
        var actions = new List<CodeAction>();

        await new ServiceTypeCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None
            )
        );

        return actions;
    }

    /// <summary>
    /// Applies the single fix offered for each DM0026 in the document, one at a time, and returns
    /// the text.
    /// </summary>
    public static async Task<string> FixEachAsync(Document document)
    {
        while (true)
        {
            var diagnostics = await DiagnosticsAsync(document.Project);
            var tree = await document.GetSyntaxTreeAsync();

            var fixable = new List<CodeAction>();

            foreach (var diagnostic in diagnostics.Where(d => d.Location.SourceTree == tree))
            {
                fixable.AddRange(await ActionsAsync(document, diagnostic));
            }

            if (fixable.Count == 0)
            {
                return (await document.GetTextAsync()).ToString();
            }

            var solution = await ApplyAsync(fixable[0]);
            document = solution.GetDocument(document.Id)!;
        }
    }

    /// <summary>
    /// Runs Fix All from <paramref name="document"/> at <paramref name="scope"/>.
    /// </summary>
    public static async Task<Solution> FixAllAsync(Document document, FixAllScope scope)
    {
        var provider = new ServiceTypeCodeFixProvider();

        var diagnostics = await DiagnosticsAsync(document.Project);
        var tree = await document.GetSyntaxTreeAsync();
        var first = diagnostics.First(diagnostic => diagnostic.Location.SourceTree == tree);
        var action = (await ActionsAsync(document, first)).Single();

        var context = new FixAllContext(
            document,
            provider,
            scope,
            action.EquivalenceKey,
            provider.FixableDiagnosticIds,
            new AnalyzerDiagnosticProvider(),
            CancellationToken.None
        );

        var fix = await provider.GetFixAllProvider()!.GetFixAsync(context);

        Assert.NotNull(fix);

        return await ApplyAsync(fix!);
    }

    private static async Task<Solution> ApplyAsync(CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);

        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }

    private sealed class AnalyzerDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken
        )
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken);

            return (await DiagnosticsAsync(document.Project)).Where(diagnostic =>
                diagnostic.Location.SourceTree == tree
            );
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => await DiagnosticsAsync(project);
    }
}

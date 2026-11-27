using DependencyModules.SourceGenerator.Impl;
using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// DM0026 and its code fix. Each fix test applies every offered fix and compares the text.
/// </summary>
public partial class ServiceTypeCodeFixTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using DependencyModules.Runtime.Attributes;
        using Single = DependencyModules.Runtime.Attributes.SingletonServiceAttribute;

        namespace TestNamespace;

        public interface IThing;

        public interface IFirst;

        public interface ISecond;

        public interface IRepo<T>;

        [DependencyModule]
        public partial class TestModule;

        """;

    [VerifyTest]
    public void TheRuleIsOffByDefault()
    {
        Verify(!DependencyModuleDiagnostics.ServiceTypeNotNamed.IsEnabledByDefault);
    }

    [VerifyTest]
    public async Task NotEnabled_ReportsNothing()
    {
        var solution = CodeFixHarness.CreateSolution(
            new[] { new[] { Preamble + "[SingletonService] public class Thing : IThing;" } },
            severity: null
        );

        var diagnostics = await CodeFixHarness.DiagnosticsAsync(solution.Projects.Single());

        Verify(diagnostics.Length == 0);
    }

    [VerifyTest]
    public async Task EnabledAsError_ReportsAnError()
    {
        var solution = CodeFixHarness.CreateSolution(
            new[] { new[] { Preamble + "[SingletonService] public class Thing : IThing;" } },
            severity: ReportDiagnostic.Error
        );

        var diagnostic = Single(await CodeFixHarness.DiagnosticsAsync(solution.Projects.Single()));

        Verify(diagnostic.Severity == DiagnosticSeverity.Error);
        Verify(
            diagnostic.GetMessage()
                == "[SingletonService] on 'Thing' does not name its service type with As = typeof(...). "
                    + "It is registered as 'IThing'."
        );
    }

    [VerifyTest]
    [Row("[SingletonService(As = typeof(IThing))] public class Thing : IThing;")]
    [Row(
        "[DependencyModules.Runtime.Attributes.ScopedService(As = typeof(Thing))] public class Thing : IThing;"
    )]
    [Row("[Single(As = typeof(IThing))] public class Thing : IThing;")]
    [Row("[CrossWireService] public class Thing : IFirst, ISecond;")]
    [Row(
        "public static class Factories { [TransientService(As = typeof(IThing))] public static Thing Create() => new(); } public class Thing : IThing;"
    )]
    public async Task ANamedTypeOrCrossWire_IsNotReported(string source)
    {
        var document = CodeFixHarness.CreateDocument(Preamble + source);

        var diagnostics = await CodeFixHarness.DiagnosticsAsync(document.Project);

        Verify(diagnostics.Length == 0);
    }

    [VerifyTest]
    // Concrete only.
    [Row(
        "[SingletonService] public class Thing;",
        "[SingletonService(As = typeof(Thing))] public class Thing;"
    )]
    [Row(
        "[SingletonService] public record Settings;",
        "[SingletonService(As = typeof(Settings))] public record Settings;"
    )]
    // Multiple interfaces: the first one wins.
    [Row(
        "[ScopedService] public class Thing : IFirst, ISecond;",
        "[ScopedService(As = typeof(IFirst))] public class Thing : IFirst, ISecond;"
    )]
    // An interface inherited from a base class.
    [Row(
        "public abstract class ThingBase : IThing; [TransientService] public class Thing : ThingBase;",
        "public abstract class ThingBase : IThing; [TransientService(As = typeof(IThing))] public class Thing : ThingBase;"
    )]
    // A capability interface alone, and beside a role interface.
    [Row(
        "[SingletonService] public class Thing : IDisposable { public void Dispose() { } }",
        "[SingletonService(As = typeof(Thing))] public class Thing : IDisposable { public void Dispose() { } }"
    )]
    [Row(
        "[SingletonService] public class Thing : IDisposable, IThing { public void Dispose() { } }",
        "[SingletonService(As = typeof(IThing))] public class Thing : IDisposable, IThing { public void Dispose() { } }"
    )]
    // Open and closed generics.
    [Row(
        "[SingletonService] public class Repo<T> : IRepo<T>;",
        "[SingletonService(As = typeof(IRepo<>))] public class Repo<T> : IRepo<T>;"
    )]
    [Row(
        "[SingletonService] public class Box<T>;",
        "[SingletonService(As = typeof(Box<>))] public class Box<T>;"
    )]
    [Row(
        "[SingletonService] public class IntRepo : IRepo<int>;",
        "[SingletonService(As = typeof(IRepo<int>))] public class IntRepo : IRepo<int>;"
    )]
    // A factory method registers its declared return type.
    [Row(
        "public class Thing : IThing; public static class Factories { [SingletonService] public static Thing Create() => new(); }",
        "public class Thing : IThing; public static class Factories { [SingletonService(As = typeof(Thing))] public static Thing Create() => new(); }"
    )]
    [Row(
        "public class Thing : IThing; public static class Factories { [ScopedService] public static IThing Create() => new Thing(); }",
        "public class Thing : IThing; public static class Factories { [ScopedService(As = typeof(IThing))] public static IThing Create() => new Thing(); }"
    )]
    // Aliased and qualified attributes.
    [Row(
        "[Single] public class Thing : IThing;",
        "[Single(As = typeof(IThing))] public class Thing : IThing;"
    )]
    [Row(
        "[global::DependencyModules.Runtime.Attributes.SingletonServiceAttribute] public class Thing : IThing;",
        "[global::DependencyModules.Runtime.Attributes.SingletonServiceAttribute(As = typeof(IThing))] public class Thing : IThing;"
    )]
    // A nested class.
    [Row(
        "public class Outer { [SingletonService] public class Inner : IThing; }",
        "public class Outer { [SingletonService(As = typeof(IThing))] public class Inner : IThing; }"
    )]
    // An As that is not typeof(...) is replaced in place.
    [Row(
        "[SingletonService(As = null, Order = 1)] public class Thing : IThing;",
        "[SingletonService(As = typeof(IThing), Order = 1)] public class Thing : IThing;"
    )]
    // Each attribute on a class is fixed.
    [Row(
        "[SingletonService] [ScopedService(Key = \"k\")] public class Thing : IThing;",
        "[SingletonService(As = typeof(IThing))] [ScopedService(Key = \"k\", As = typeof(IThing))] public class Thing : IThing;"
    )]
    public async Task TheFix_NamesTheInferredType(string before, string after)
    {
        var fixedText = await CodeFixHarness.FixEachAsync(
            CodeFixHarness.CreateDocument(Preamble + before)
        );

        Verify(fixedText == Preamble + after);
    }

    [VerifyTest]
    [Row(
        """
            [SingletonService(Key = "k" /* key */, Using = RegistrationType.Try, Realm = typeof(TestModule), Order = 2)] // keep
            public class Thing : IThing;
            """,
        """
            [SingletonService(Key = "k" /* key */, Using = RegistrationType.Try, Realm = typeof(TestModule), Order = 2, As = typeof(IThing))] // keep
            public class Thing : IThing;
            """
    )]
    [Row(
        """
            [SingletonService(
                Key = "k", // the key
                Order = 2
            )]
            public class Thing : IThing;
            """,
        """
            [SingletonService(
                Key = "k", // the key
                Order = 2, As = typeof(IThing)
            )]
            public class Thing : IThing;
            """
    )]
    [Row(
        "[SingletonService /* note */] public class Thing : IThing;",
        "[SingletonService(As = typeof(IThing)) /* note */] public class Thing : IThing;"
    )]
    [Row(
        "[SingletonService()] public class Thing : IThing;",
        "[SingletonService(As = typeof(IThing))] public class Thing : IThing;"
    )]
    public async Task TheFix_KeepsOtherArgumentsAndTrivia(string before, string after)
    {
        var fixedText = await CodeFixHarness.FixEachAsync(
            CodeFixHarness.CreateDocument(Preamble + before)
        );

        Verify(fixedText == Preamble + after);
    }

    [VerifyTest]
    public async Task TheFix_IsTitledWithTheType()
    {
        var document = CodeFixHarness.CreateDocument(
            Preamble + "[SingletonService] public class Repo<T> : IRepo<T>;"
        );
        var diagnostic = Single(await CodeFixHarness.DiagnosticsAsync(document.Project));

        var action = Single(await CodeFixHarness.ActionsAsync(document, diagnostic));

        Verify(action.Title == "Make service type explicit: IRepo<>");
    }

    /// <summary>
    /// The diagnostic still reports when the type cannot be written as typeof(...) at the
    /// attribute, or cannot be determined at all.
    /// </summary>
    [VerifyTest]
    [Row(
        "public interface IMap<TKey, TValue>; [SingletonService] public class Map<T> : IMap<T, int>;"
    )]
    [Row("[SingletonService] public class Wrapped<T> : IRepo<List<T>>;")]
    [Row(
        "public static class Factories { [SingletonService] public static Missing Create() => null!; }"
    )]
    public async Task AnUnwritableType_IsReportedWithNoFix(string source)
    {
        var document = CodeFixHarness.CreateDocument(Preamble + source);
        var diagnostic = Single(await CodeFixHarness.DiagnosticsAsync(document.Project));

        var actions = await CodeFixHarness.ActionsAsync(document, diagnostic);

        Verify(actions.Count == 0);
    }

    [VerifyTest]
    public async Task AnUndeterminedType_SaysSo()
    {
        var document = CodeFixHarness.CreateDocument(
            Preamble
                + "public static class Factories { [SingletonService(As = null)] public static Missing Create() => null!; }"
        );
        var diagnostic = Single(await CodeFixHarness.DiagnosticsAsync(document.Project));

        Verify(
            diagnostic.GetMessage()
                == "[SingletonService] on 'Create' does not name its service type with As = typeof(...). "
                    + "The generator ignores an As that is not typeof(...), so the service type is inferred. "
                    + "The service type cannot be determined."
        );
    }

    [VerifyTest]
    [Row(FixAllScope.Document, 0, 1, 1)]
    [Row(FixAllScope.Project, 0, 0, 1)]
    [Row(FixAllScope.Solution, 0, 0, 0)]
    public async Task FixAll_FixesItsScope(
        FixAllScope scope,
        int leftInFirst,
        int leftInSecond,
        int leftInOtherProject
    )
    {
        var solution = CodeFixHarness.CreateSolution(
            new[]
            {
                new[]
                {
                    Preamble
                        + "[SingletonService] public class A1 : IThing; [ScopedService] public class A2;",
                    "using DependencyModules.Runtime.Attributes; namespace TestNamespace; [TransientService] public class B1 : IFirst;",
                },
                new[] { Preamble + "[SingletonService] public class C1 : ISecond;" },
            }
        );

        var first = solution.Projects.First().Documents.First();

        var fixedSolution = await CodeFixHarness.FixAllAsync(first, scope);

        var left = new List<int>();

        foreach (var project in fixedSolution.Projects)
        {
            var diagnostics = await CodeFixHarness.DiagnosticsAsync(project);

            foreach (var document in project.Documents)
            {
                var tree = await document.GetSyntaxTreeAsync();
                left.Add(diagnostics.Count(diagnostic => diagnostic.Location.SourceTree == tree));
            }
        }

        Verify(left.SequenceEqual([leftInFirst, leftInSecond, leftInOtherProject]));

        var firstText = (await fixedSolution.GetDocument(first.Id)!.GetTextAsync()).ToString();
        Verify(
            firstText.EndsWith(
                "[SingletonService(As = typeof(IThing))] public class A1 : IThing; "
                    + "[ScopedService(As = typeof(A2))] public class A2;",
                StringComparison.Ordinal
            )
        );
    }

    /// <summary>
    /// Generates the module, applies every fix, generates again, and compares what the container
    /// gets.
    /// </summary>
    [VerifyTest]
    public async Task ApplyingEveryFix_LeavesTheRegistrationsUnchanged()
    {
        const string registrations = """
            [SingletonService] public class Concrete;

            [SingletonService] public record Settings;

            [ScopedService] public class Multiple : IFirst, ISecond;

            public abstract class ThingBase : IThing;

            [TransientService(Key = "inherited")] public class Inherited : ThingBase;

            [SingletonService] public class Disposable : IDisposable { public void Dispose() { } }

            [ScopedService(Key = "role")] public class DisposableThing : IDisposable, IThing { public void Dispose() { } }

            [ScopedService] public class Repo<T> : IRepo<T>;

            [TransientService(Key = "ints")] public class IntRepo : IRepo<int>;

            [Single(Using = RegistrationType.Try, Order = 1)] public class Aliased : ISecond;

            [SingletonService(As = null)] public class InvalidAs : IFirst;

            public class Made : IThing;

            public static class Factories
            {
                [SingletonService] public static Made Create() => new();

                [TransientService] public static IThing CreateThing() => new Made();
            }
            """;

        var before = Preamble + registrations;
        var after = await CodeFixHarness.FixEachAsync(CodeFixHarness.CreateDocument(before));

        var diagnostics = await CodeFixHarness.DiagnosticsAsync(
            CodeFixHarness.CreateDocument(after).Project
        );

        Verify(diagnostics.Length == 0);
        Verify(after.Split("As = typeof(").Length - 1 == 12);

        Verify(
            Registrations(GeneratedAssembly.Create(after))
                .SequenceEqual(Registrations(GeneratedAssembly.Create(before)))
        );
    }

    private static IReadOnlyList<string> Registrations(GeneratedAssembly assembly) =>
        assembly
            .Services.Where(descriptor => descriptor.ServiceType.Namespace == "TestNamespace")
            .Select(descriptor =>
                $"{descriptor.ServiceType.FullName} {descriptor.Lifetime} {descriptor.ServiceKey} "
                + (
                    descriptor.IsKeyedService
                        ? descriptor.KeyedImplementationType?.FullName
                        : descriptor.ImplementationType?.FullName
                )
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
}

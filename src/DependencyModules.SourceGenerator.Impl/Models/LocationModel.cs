using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// Where a declaration sits in source, in a form that can live in an incremental model.
/// </summary>
/// <remarks>
/// A <see cref="Location"/> cannot be stored in a model. It holds a reference to its
/// <see cref="SyntaxTree"/>, so caching one pins the whole tree in memory and the model never
/// compares equal across runs — the same reason symbols are rendered to strings in the transform.
/// This carries the primitives instead and rebuilds the location at output time, which is the point
/// diagnostics are actually reported.
/// </remarks>
public record LocationModel(
    string FilePath,
    int SpanStart,
    int SpanLength,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter) {

    /// <summary>
    /// Rebuilds a reportable location. Safe to call only outside the incremental pipeline.
    /// </summary>
    /// <remarks>
    /// This is the external-file overload of <see cref="Location.Create(string, TextSpan, LinePositionSpan)"/>,
    /// so the result reports the right file, line and column but has no <c>SourceTree</c>. Roslyn
    /// keys <c>.editorconfig</c> severity and <c>#pragma warning</c> off the tree, so a diagnostic
    /// reported here can only be silenced with <c>NoWarn</c>. Prefer
    /// <see cref="ToLocation(SyntaxTreeLookup)"/>, which attaches the tree when it can find it.
    /// </remarks>
    public Location ToLocation() =>
        Location.Create(
            FilePath,
            new TextSpan(SpanStart, SpanLength),
            new LinePositionSpan(
                new LinePosition(StartLine, StartCharacter),
                new LinePosition(EndLine, EndCharacter)));

    /// <summary>
    /// Rebuilds a reportable location against the syntax tree it came from, so that
    /// <c>.editorconfig</c> and <c>#pragma warning disable</c> can reach the diagnostic.
    /// </summary>
    /// <remarks>
    /// Falls back to the tree-less form when the file is not in the compilation, or when the span
    /// no longer fits inside it. The second case is real rather than defensive: a model's location
    /// is deliberately left out of the incremental comparers — it moves whenever anything above the
    /// declaration is edited, and including it regenerated every model on a comment keystroke — so
    /// a replayed model can carry a span from a previous version of the file. Out of bounds,
    /// <c>Location.Create</c> throws, and a generator that throws reports nothing at all.
    /// </remarks>
    public Location ToLocation(SyntaxTreeLookup lookup) {
        var tree = lookup.Find(FilePath);

        if (tree == null) {
            return ToLocation();
        }

        var span = new TextSpan(SpanStart, SpanLength);

        return span.End <= tree.Length ? Location.Create(tree, span) : ToLocation();
    }

    public static LocationModel From(SyntaxNode node) => From(NarrowToName(node));

    /// <summary>
    /// The span a diagnostic about a declaration should point at: its name, not its whole body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Squiggling the identifier is the better affordance on its own — DM0006 and DM0010 are about
    /// the type, not about everything inside it — but the reason it is done here is caching.
    /// </para>
    /// <para>
    /// This model takes part in <see cref="ConventionCandidateModel"/> equality, and the declaration's
    /// full span changes length whenever anything inside the class is edited. Keyed on the whole
    /// declaration, typing inside any method body produced a model that no longer compared equal, so
    /// the convention matcher re-ran over every candidate and re-rendered the file to produce
    /// identical text. The identifier does not move when a body below it is edited, so the common
    /// keystroke now changes nothing.
    /// </para>
    /// </remarks>
    private static SyntaxNodeOrToken NarrowToName(SyntaxNode node) =>
        node switch {
            TypeDeclarationSyntax type => type.Identifier,
            MethodDeclarationSyntax method => method.Identifier,
            _ => node
        };

    private static LocationModel From(SyntaxNodeOrToken nodeOrToken) {
        var span = nodeOrToken.GetLocation()!.GetLineSpan();

        return new LocationModel(
            nodeOrToken.SyntaxTree!.FilePath,
            nodeOrToken.Span.Start,
            nodeOrToken.Span.Length,
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }

    /// <summary>
    /// Stands in for a declaration with no meaningful location, so the model stays non-nullable.
    /// </summary>
    public static readonly LocationModel None = new("", 0, 0, 0, 0, 0, 0);

    public Location ToLocationOrNone() => this == None ? Location.None : ToLocation();

    /// <inheritdoc cref="ToLocationOrNone()"/>
    public Location ToLocationOrNone(SyntaxTreeLookup lookup) =>
        this == None ? Location.None : ToLocation(lookup);
}

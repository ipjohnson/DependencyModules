using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DependencyModules.Conventions.Models;

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
    public Location ToLocation() =>
        Location.Create(
            FilePath,
            new TextSpan(SpanStart, SpanLength),
            new LinePositionSpan(
                new LinePosition(StartLine, StartCharacter),
                new LinePosition(EndLine, EndCharacter)));

    public static LocationModel From(SyntaxNode node) {
        var span = node.GetLocation().GetLineSpan();

        return new LocationModel(
            node.SyntaxTree.FilePath,
            node.Span.Start,
            node.Span.Length,
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
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
}

using System;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// Where a diagnostic goes, and how its location is rebuilt on the way.
/// </summary>
/// <remarks>
/// <para>
/// The two travel together on purpose. A <see cref="LocationModel"/> can be turned into a location
/// with or without the syntax tree it came from, and only the version with the tree can be silenced
/// by <c>.editorconfig</c> or <c>#pragma warning disable</c> — so a reporting site that is handed a
/// bare sink is a site that can quietly produce a diagnostic nobody can turn off. Handing both over
/// as one thing removes the choice.
/// </para>
/// <para>
/// <see cref="Silent"/> exists because analysis that reports is also analysis that produces
/// something else worth having. The convention matcher is run twice — once to emit registrations,
/// once to report on them — and the emitting pass wants none of the diagnostics. Asking
/// <see cref="IsSilent"/> first lets a caller skip building a message it is about to discard.
/// </para>
/// </remarks>
public sealed class DiagnosticReporter {
    private readonly Action<Diagnostic>? _sink;
    private readonly SyntaxTreeLookup _lookup;

    /// <summary>A reporter that discards everything.</summary>
    public static readonly DiagnosticReporter Silent = new(null, SyntaxTreeLookup.None);

    public DiagnosticReporter(Action<Diagnostic>? sink, SyntaxTreeLookup lookup) {
        _sink = sink;
        _lookup = lookup;
    }

    /// <summary>True when nothing is listening, so there is no point composing a message.</summary>
    public bool IsSilent => _sink == null;

    public void Report(DiagnosticDescriptor descriptor, LocationModel? location, params object?[] messageArgs) {
        if (_sink == null) {
            return;
        }

        _sink(Diagnostic.Create(
            descriptor,
            location?.ToLocationOrNone(_lookup) ?? Location.None,
            messageArgs));
    }

    /// <summary>
    /// Reports at a location that is already resolved — one read straight from syntax rather than
    /// carried through a model.
    /// </summary>
    public void Report(DiagnosticDescriptor descriptor, Location location, params object?[] messageArgs) {
        _sink?.Invoke(Diagnostic.Create(descriptor, location, messageArgs));
    }

    /// <summary>
    /// Rebuilds a location the way this reporter would, for a caller that has to hand one to
    /// something else.
    /// </summary>
    public Location LocationFor(LocationModel? location) =>
        location?.ToLocationOrNone(_lookup) ?? Location.None;
}

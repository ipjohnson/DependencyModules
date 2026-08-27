using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// Finds the syntax tree a file path belongs to, so a <see cref="LocationModel"/> can be turned
/// back into a location Roslyn will let the developer silence.
/// </summary>
/// <remarks>
/// Built where diagnostics are reported and thrown away again, rather than cached. Holding one
/// across the incremental pipeline would pin every tree in the compilation, which is the whole
/// reason <see cref="LocationModel"/> carries primitives instead of a location; and a cached one
/// would answer with trees from a compilation that no longer exists, which is worse than answering
/// with nothing. The map is built on first use, so a run that reports no diagnostics — the ordinary
/// case — never walks the trees at all.
/// </remarks>
public sealed class SyntaxTreeLookup {
    private readonly Compilation? _compilation;
    private Dictionary<string, SyntaxTree>? _byPath;

    /// <summary>A lookup that finds nothing, for callers with no compilation to hand.</summary>
    public static readonly SyntaxTreeLookup None = new(null);

    public SyntaxTreeLookup(Compilation? compilation) {
        _compilation = compilation;
    }

    public SyntaxTree? Find(string filePath) {
        if (_compilation == null || string.IsNullOrEmpty(filePath)) {
            return null;
        }

        if (_byPath == null) {
            _byPath = new Dictionary<string, SyntaxTree>();

            foreach (var tree in _compilation.SyntaxTrees) {
                // First wins. Two trees can share a path — a linked file compiled into more than
                // one target — and either answers the question a location asks.
                if (!string.IsNullOrEmpty(tree.FilePath) && !_byPath.ContainsKey(tree.FilePath)) {
                    _byPath.Add(tree.FilePath, tree);
                }
            }
        }

        return _byPath.TryGetValue(filePath, out var found) ? found : null;
    }
}
